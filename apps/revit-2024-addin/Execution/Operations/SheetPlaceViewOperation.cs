using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>sheet.place_view</c>: places a view on a sheet.</summary>
    public static class SheetPlaceViewOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;

            Element sheetElement = ElementResolver.Resolve(document, args["sheet"], results);
            ViewSheet sheet = sheetElement as ViewSheet;
            if (sheet == null)
            {
                throw new AgentException(ErrorCodes.CategoryMismatch,
                    "The target element is not a sheet.", true);
            }

            Element viewElement = ElementResolver.Resolve(document, args["view"], results);
            View view = viewElement as View;
            if (view == null)
            {
                throw new AgentException(ErrorCodes.CategoryMismatch,
                    "The target element is not a view.", true);
            }

            // Check if the view is already placed on a sheet.
            if (sheet.GetAllPlacedViews().Contains(view.Id))
            {
                throw new AgentException(ErrorCodes.InvalidArgument,
                    "View is already placed on this sheet.", true);
            }

            JsonValue pointJson = args["point"];
            double scale = UnitNames.FeetPerUnit(plan.Units);
            var location = new XYZ(
                pointJson["x"].AsDouble() * scale,
                pointJson["y"].AsDouble() * scale,
                0);

            Viewport viewport = Viewport.Create(document, sheet.Id, view.Id, location);

            return new OperationResult(
                operation.Id,
                "sheet.place_view",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(viewport) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["sheet"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(sheet.Id.Value),
                        ["sheetNumber"] = JsonValue.String(sheet.SheetNumber)
                    }),
                    ["view"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(view.Id.Value),
                        ["name"] = JsonValue.String(view.Name)
                    })
                }));
        }
    }
}
