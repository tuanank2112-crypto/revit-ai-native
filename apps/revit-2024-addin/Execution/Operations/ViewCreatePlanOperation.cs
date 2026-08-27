using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>view.create_plan</c>: creates a floor plan or ceiling plan view.</summary>
    public static class ViewCreatePlanOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            string name = args["name"].AsString(null);
            string viewTypeReq = args["viewType"].AsString("floor_plan");
            ViewFamilyType familyType = null;

            // Find the matching ViewFamilyType.
            ViewFamily targetFamily = viewTypeReq == "ceiling_plan" ? ViewFamily.CeilingPlan : ViewFamily.FloorPlan;
            var types = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType));
            foreach (ViewFamilyType vft in types)
            {
                if (vft.ViewFamily == targetFamily)
                {
                    familyType = vft;
                    break;
                }
            }

            if (familyType == null)
            {
                throw new AgentException(ErrorCodes.TypeNotFound,
                    "No ViewFamilyType found for " + targetFamily + ".", true);
            }

            Level level = LevelResolver.Resolve(document, args["level"], plan.Units, document.ActiveView).Value;

            // Check name uniqueness.
            string viewName = name ?? (level.Name + " - " + viewTypeReq);
            var existingViews = new FilteredElementCollector(document).OfClass(typeof(View));
            foreach (View v in existingViews)
            {
                if (string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AgentException(ErrorCodes.InvalidArgument,
                        "A view named '" + viewName + "' already exists.", true);
                }
            }

            ViewPlan newView = ViewPlan.Create(document, familyType.Id, level.Id);
            newView.Name = viewName;

            return new OperationResult(
                operation.Id,
                "view.create_plan",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(newView) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name)
                    }),
                    ["viewFamilyType"] = JsonValue.String(targetFamily.ToString())
                }));
        }
    }
}
