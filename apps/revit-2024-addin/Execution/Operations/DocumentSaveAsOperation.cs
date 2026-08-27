using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>document.save_as</c>: saves the active document to a new path.
    /// This is a sensitive operation requiring confirmation, path validation, and
    /// overwrite protection.
    /// </summary>
    public static class DocumentSaveAsOperation
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
            string rawPath = args["path"].AsString(null);

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "Save-as path is empty.", true);
            }

            // Validate path: canonicalize, check extension, prevent traversal.
            SecurityValidator.PathCheckResult pathCheck = SecurityValidator.ValidateSaveAsPath(rawPath);
            if (!pathCheck.Allowed)
            {
                throw new AgentException(ErrorCodes.PathNotAllowed,
                    pathCheck.Error?.Message ?? "Path is not allowed.", true);
            }

            string canonicalPath = pathCheck.CanonicalPath;

            // Enforce .rvt extension.
            string extension = Path.GetExtension(canonicalPath);
            if (!string.Equals(extension, ".rvt", StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentException(ErrorCodes.InvalidArgument,
                    "Save-as path must end with .rvt", true);
            }

            // Overwrite policy.
            bool overwrite = args["overwrite"].AsBool(false);
            if (File.Exists(canonicalPath) && !overwrite)
            {
                throw new AgentException(ErrorCodes.FileAlreadyExists,
                    "File already exists: " + Path.GetFileName(canonicalPath), true,
                    "Set overwrite=true or use a different filename.");
            }

            // Backup policy.
            bool backup = args["backup"].AsBool(false);

            var sw = Stopwatch.StartNew();

            var saveAsOptions = new SaveAsOptions
            {
                OverwriteExistingFile = overwrite,
                MaximumBackups = backup ? 20 : 1
            };

            document.SaveAs(canonicalPath, saveAsOptions);

            sw.Stop();

            return new OperationResult(
                operation.Id,
                "document.save_as",
                OperationOutcome.Completed,
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["saved"] = JsonValue.Bool(true),
                    ["documentTitle"] = JsonValue.String(document.Title ?? string.Empty),
                    ["documentPath"] = JsonValue.String(Path.GetFileName(canonicalPath)),
                    ["overwritten"] = JsonValue.Bool(overwrite),
                    ["durationMs"] = JsonValue.Number(sw.ElapsedMilliseconds)
                }));
        }
    }
}
