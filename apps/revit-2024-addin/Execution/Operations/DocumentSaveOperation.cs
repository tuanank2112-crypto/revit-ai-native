using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>document.save</c>: saves the active document to its current path.</summary>
    public static class DocumentSaveOperation
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

            if (document.IsReadOnly)
            {
                throw new AgentException(ErrorCodes.DocumentReadOnly, "Document is read-only.", false);
            }

            string currentPath = document.PathName;
            if (string.IsNullOrEmpty(currentPath))
            {
                throw new AgentException(ErrorCodes.InvalidArgument,
                    "Document has no path. Use document.save_as instead.", true);
            }

            var sw = Stopwatch.StartNew();

            document.Save();

            sw.Stop();

            string sanitizedPath = SanitizePath(currentPath);

            return new OperationResult(
                operation.Id,
                "document.save",
                OperationOutcome.Completed,
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["saved"] = JsonValue.Bool(true),
                    ["documentTitle"] = JsonValue.String(document.Title ?? string.Empty),
                    ["documentPath"] = JsonValue.String(sanitizedPath),
                    ["durationMs"] = JsonValue.Number(sw.ElapsedMilliseconds)
                }));
        }

        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            // Redact the directory portion; keep filename only for audit safety.
            return System.IO.Path.GetFileName(path) ?? path;
        }
    }
}
