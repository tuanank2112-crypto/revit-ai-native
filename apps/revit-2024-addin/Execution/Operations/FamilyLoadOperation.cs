using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>family.load</c>: loads a .rfa/.fam family file into the document so its
    /// symbols can be placed by <c>family.instance.create</c> / <c>column.create</c>.
    ///
    /// Verified overloads (this Revit 2024 build, declared on Document):
    /// <c>LoadFamilySymbol(String filename, String name, out FamilySymbol symbol) : Boolean</c>
    /// and <c>LoadFamily(Document, out Family)</c> — the latter takes a Document, so the string
    /// path form used here is <c>LoadFamilySymbol</c> (which requires a symbol name). When the
    /// symbol name is omitted, the family is loaded by its filename-derived name; a false return
    /// (family already present) is surfaced as a warning, not a failure.
    /// </summary>
    public static class FamilyLoadOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            string filename = args["path"].AsString(null);
            if (string.IsNullOrEmpty(filename))
            {
                throw new AgentException(ErrorCodes.MissingArgument, "family.load requires 'path' (full .rfa/.fam path).", true);
            }

            if (!System.IO.File.Exists(filename))
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "Family file not found: " + filename, true);
            }

            string symbolName = args["symbolName"].AsString(null);
            if (string.IsNullOrEmpty(symbolName))
            {
                // Derive a name from the file (Revit family symbol names usually mirror this).
                symbolName = System.IO.Path.GetFileNameWithoutExtension(filename);
            }

            var warnings = new List<string>();
            bool loaded = document.LoadFamilySymbol(filename, symbolName, out FamilySymbol symbol);
            document.Regenerate();

            string familyOut = null;
            if (!loaded)
            {
                // false = not newly loaded: usually already present in the document.
                symbol = FindSymbolByName(document, symbolName);
                if (symbol == null)
                {
                    throw new AgentException(ErrorCodes.ExecutionFailed,
                        "family.load: LoadFamilySymbol('" + symbolName + "') failed and no matching symbol exists in the document.",
                        true);
                }

                warnings.Add("family already loaded (no new symbol added).");
            }

            if (symbol != null)
            {
                familyOut = symbol.FamilyName;
            }

            return new OperationResult(
                operation.Id,
                "family.load",
                OperationOutcome.Completed,
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["path"] = JsonValue.String(filename),
                    ["family"] = JsonValue.String(familyOut ?? string.Empty),
                    ["symbol"] = JsonValue.String(symbol != null ? symbol.Name : string.Empty),
                    ["newlyLoaded"] = JsonValue.Bool(loaded)
                }),
                warnings: warnings);
        }

        private static FamilySymbol FindSymbolByName(Document document, string name)
        {
            foreach (FamilySymbol s in new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)))
            {
                if (string.Equals(s.Name, name, StringComparison.Ordinal))
                {
                    return s;
                }
            }

            return null;
        }
    }
}
