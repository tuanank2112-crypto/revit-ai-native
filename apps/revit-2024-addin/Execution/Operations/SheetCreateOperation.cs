using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>sheet.create</c>: creates a new sheet with an optional title block.</summary>
    public static class SheetCreateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            string sheetNumber = args["sheetNumber"].AsString(null);
            string sheetName = args["sheetName"].AsString(null);

            if (string.IsNullOrEmpty(sheetNumber))
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "sheetNumber is required.", true);
            }

            // Resolve title block.
            ElementId titleBlockId = ElementId.InvalidElementId;
            JsonValue tbSelector = args["titleBlock"];

            var titleBlocks = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType();

            if (tbSelector != null && !tbSelector.IsNull)
            {
                string strategy = tbSelector["strategy"].AsString(null);
                if (strategy == "exact_name")
                {
                    string tbName = tbSelector["name"].AsString(null);
                    foreach (Element tb in titleBlocks)
                    {
                        if (string.Equals(tb.Name, tbName, StringComparison.OrdinalIgnoreCase))
                        {
                            titleBlockId = tb.Id;
                            break;
                        }
                    }
                }
            }
            else
            {
                // If no title block specified and exactly one exists, use it.
                // Otherwise fail — we do not randomly pick when ambiguous.
                int count = titleBlocks.Count();
                if (count == 1)
                {
                    titleBlockId = titleBlocks.First().Id;
                }
                else if (count == 0)
                {
                    // No title block; create without one.
                    titleBlockId = ElementId.InvalidElementId;
                }
                else
                {
                    throw new AgentException(ErrorCodes.AmbiguousType,
                        "Multiple title blocks found; specify one with titleBlock.strategy=exact_name.", true);
                }
            }

            ViewSheet sheet = ViewSheet.Create(document, titleBlockId);
            sheet.SheetNumber = sheetNumber;
            if (!string.IsNullOrEmpty(sheetName))
            {
                sheet.Name = sheetName;
            }

            return new OperationResult(
                operation.Id,
                "sheet.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(sheet) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["sheetNumber"] = JsonValue.String(sheet.SheetNumber),
                    ["sheetName"] = JsonValue.String(sheet.Name)
                }));
        }
    }
}
