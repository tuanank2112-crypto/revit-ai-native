using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>export.pdf</c>: exports selected views/sheets to PDF using the
    /// Revit 2024 Document.Export(string, IList&lt;ElementId&gt;, PDFExportOptions) overload.
    /// </summary>
    public static class ExportPdfOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            JsonValue args = operation.Args;
            string outputDir = args["outputDirectory"].AsString(null);

            if (string.IsNullOrWhiteSpace(outputDir))
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "outputDirectory is required.", true);
            }

            if (!Directory.Exists(outputDir))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                }
                catch
                {
                    throw new AgentException(ErrorCodes.PathNotAllowed,
                        "Cannot create output directory: " + outputDir, true);
                }
            }

            // Resolve views/sheets to export.
            JsonValue viewRefs = args["views"];
            if (!viewRefs.IsArray || viewRefs.Count == 0)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "At least one view/sheet must be specified.", true);
            }

            var viewIds = new List<ElementId>();
            var exportedViews = new List<JsonValue>();

            foreach (JsonValue viewRef in viewRefs.Items)
            {
                Element element = ElementResolver.Resolve(document, viewRef, results);
                View view = element as View;
                if (view == null)
                {
                    throw new AgentException(ErrorCodes.CategoryMismatch,
                        "Element is not a view or sheet.", true);
                }

                // Revit 2024 PDF export requires printable views (not templates).
                if (view.IsTemplate)
                {
                    throw new AgentException(ErrorCodes.InvalidArgument,
                        "Cannot export a view template: " + view.Name, true);
                }

                viewIds.Add(view.Id);
                exportedViews.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(view.Id.Value),
                    ["name"] = JsonValue.String(view.Name ?? string.Empty)
                }));
            }

            string baseName = args["baseName"].AsString("export");
            bool overwrite = args["overwrite"].AsBool(false);
            bool combine = args["combine"].AsBool(false);

            string fullPath = Path.Combine(outputDir, baseName + ".pdf");
            if (File.Exists(fullPath) && !overwrite)
            {
                throw new AgentException(ErrorCodes.FileAlreadyExists,
                    "File already exists: " + baseName + ".pdf", true);
            }

            // PDF export options for Revit 2024.
            var pdfOptions = new PDFExportOptions
            {
                FileName = baseName,
                Combine = combine,
                ExportQuality = GetPdfQuality(args["quality"].AsString("DPI300")),
                RasterQuality = GetRasterQuality(args["rasterQuality"].AsString("medium")),
                PaperOrientation = GetPaperOrientation(args["orientation"].AsString("landscape")),
                PaperFormat = GetPaperFormat(args["paperSize"].AsString("A1"))
            };

            // Revit 2024 supports color/mask options.
            if (!args["color"].IsNull)
            {
                pdfOptions.ColorDepth = GetColorDepth(args["color"].AsString("color"));
            }

            var sw = Stopwatch.StartNew();

            // Revit 2024 PDF export API: document.Export(string folder, IList<ElementId> views, PDFExportOptions options)
            document.Export(outputDir, viewIds, pdfOptions);

            sw.Stop();

            var exportedFiles = new List<JsonValue>();
            if (combine)
            {
                string combinedPath = Path.Combine(outputDir, baseName + ".pdf");
                if (File.Exists(combinedPath))
                {
                    exportedFiles.Add(JsonValue.String(Path.GetFileName(combinedPath)));
                }
            }
            else
            {
                // Non-combined: one PDF per view.
                var dirInfo = new DirectoryInfo(outputDir);
                foreach (FileInfo fi in dirInfo.GetFiles(baseName + "*.pdf"))
                {
                    exportedFiles.Add(JsonValue.String(fi.Name));
                }
            }

            return new OperationResult(
                operation.Id,
                "export.pdf",
                OperationOutcome.Completed,
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["exportedFiles"] = JsonValue.Array(exportedFiles),
                    ["views"] = JsonValue.Array(exportedViews),
                    ["outputDirectory"] = JsonValue.String(outputDir),
                    ["combine"] = JsonValue.Bool(combine),
                    ["durationMs"] = JsonValue.Number(sw.ElapsedMilliseconds)
                }));
        }

        private static RasterQualityType GetRasterQuality(string quality)
        {
            switch (quality?.ToLowerInvariant())
            {
                case "low": return RasterQualityType.Low;
                case "high": return RasterQualityType.High;
                case "presentation": return RasterQualityType.Presentation;
                default: return RasterQualityType.Medium;
            }
        }

        private static PDFExportQualityType GetPdfQuality(string quality)
        {
            switch (quality?.ToUpperInvariant())
            {
                case "LOW":
                case "DPI72": return PDFExportQualityType.DPI72;
                case "HIGH":
                case "DPI600": return PDFExportQualityType.DPI600;
                case "DPI1200": return PDFExportQualityType.DPI1200;
                case "DPI2400": return PDFExportQualityType.DPI2400;
                case "DPI3600": return PDFExportQualityType.DPI3600;
                case "DPI4000": return PDFExportQualityType.DPI4000;
                case "DPI144": return PDFExportQualityType.DPI144;
                default: return PDFExportQualityType.DPI300;
            }
        }

        private static PageOrientationType GetPaperOrientation(string orientation)
        {
            switch (orientation?.ToLowerInvariant())
            {
                case "portrait": return PageOrientationType.Portrait;
                default: return PageOrientationType.Landscape;
            }
        }

        private static ExportPaperFormat GetPaperFormat(string size)
        {
            switch (size?.ToUpperInvariant())
            {
                case "A0": return ExportPaperFormat.ISO_A0;
                case "A2": return ExportPaperFormat.ISO_A2;
                case "A3": return ExportPaperFormat.ISO_A3;
                case "A4": return ExportPaperFormat.ISO_A4;
                case "LETTER": return ExportPaperFormat.ANSI_A;
                case "TABLOID": return ExportPaperFormat.ANSI_B;
                case "LEGAL": return ExportPaperFormat.ANSI_A;
                case "LEDGER": return ExportPaperFormat.ANSI_B;
                default: return ExportPaperFormat.ISO_A1;
            }
        }

        private static ColorDepthType GetColorDepth(string color)
        {
            switch (color?.ToLowerInvariant())
            {
                case "grayscale": return ColorDepthType.GrayScale;
                case "black": return ColorDepthType.BlackLine;
                default: return ColorDepthType.Color;
            }
        }
    }
}
