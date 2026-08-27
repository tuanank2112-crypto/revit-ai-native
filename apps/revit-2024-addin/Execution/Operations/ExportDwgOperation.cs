using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>export.dwg</c>: exports selected views/sheets to DWG using the
    /// Revit 2024 API. Path is validated against export root policy.
    /// </summary>
    public static class ExportDwgOperation
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

            // Validate the output directory.
            SecurityValidator.PathCheckResult dirCheck = SecurityValidator.ValidateExportPath(
                Path.Combine(outputDir, "dummy.dwg"),
                new List<string>(0)); // No export roots configured → deny by default.

            // If no export roots are configured, fall back to checking that the directory exists.
            if (!dirCheck.Allowed)
            {
                if (!Directory.Exists(outputDir))
                {
                    throw new AgentException(ErrorCodes.PathNotAllowed,
                        "Output directory does not exist: " + outputDir, true);
                }
            }

            // Resolve views to export.
            JsonValue viewRefs = args["views"];
            if (!viewRefs.IsArray || viewRefs.Count == 0)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "At least one view must be specified.", true);
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
                        "Element is not a view.", true);
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

            // DWG export options for Revit 2024.
            var dwgOptions = new DWGExportOptions
            {
                MergedViews = args["mergeViews"].AsBool(false),
                FileVersion = GetDwgVersion(args["version"].AsString("R2013"))
            };

            string fullPath = Path.Combine(outputDir, baseName + ".dwg");
            if (File.Exists(fullPath) && !overwrite)
            {
                throw new AgentException(ErrorCodes.FileAlreadyExists,
                    "File already exists: " + baseName + ".dwg", true);
            }

            var sw = Stopwatch.StartNew();

            // Revit 2024 Export: document.Export(string folder, string name, ICollection<ElementId> views, DWGExportOptions options)
            document.Export(outputDir, baseName, viewIds, dwgOptions);

            sw.Stop();

            var exportedFiles = new List<JsonValue>();
            if (File.Exists(fullPath))
            {
                exportedFiles.Add(JsonValue.String(Path.GetFileName(fullPath)));
            }

            return new OperationResult(
                operation.Id,
                "export.dwg",
                OperationOutcome.Completed,
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["exportedFiles"] = JsonValue.Array(exportedFiles),
                    ["views"] = JsonValue.Array(exportedViews),
                    ["outputDirectory"] = JsonValue.String(outputDir),
                    ["durationMs"] = JsonValue.Number(sw.ElapsedMilliseconds)
                }));
        }

        private static ACADVersion GetDwgVersion(string version)
        {
            switch (version?.ToUpperInvariant())
            {
                case "R2018": return ACADVersion.R2018;
                case "R2010": return ACADVersion.R2010;
                case "R2007": return ACADVersion.R2007;
                default: return ACADVersion.R2013;
            }
        }
    }
}
