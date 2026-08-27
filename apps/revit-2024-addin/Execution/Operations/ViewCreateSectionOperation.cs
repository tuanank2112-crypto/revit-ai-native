using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>view.create_section</c>: creates a section view through a rectangular box.
    /// Used for building sections (and, with a box oriented vertically, elevations).
    ///
    /// Verified by compile probe #12: the factory is
    /// <c>ViewSection.CreateSection(Document, ElementId viewFamilyTypeId, BoundingBoxXYZ)</c>
    /// (there is no <c>ViewSection.Create</c> in this build), and <c>BoundingBoxXYZ</c> only
    /// exposes <c>Min</c>/<c>Max</c> here — <c>Workplane</c> and <c>BoundsType</c> are absent,
    /// so the section box is defined purely by its min/max corners.
    /// </summary>
    public static class ViewCreateSectionOperation
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
            string viewType = args["viewType"].AsString("section");
            ViewFamily targetFamily = viewType == "elevation" ? ViewFamily.Elevation : ViewFamily.Section;

            ViewFamilyType vft = FindViewFamilyType(document, targetFamily);

            JsonValue box = args["box"];
            double scale = UnitNames.FeetPerUnit(plan.Units);
            XYZ min = new XYZ(box["min"]["x"].AsDouble() * scale, box["min"]["y"].AsDouble() * scale, box["min"]["z"].AsDouble(0) * scale);
            XYZ max = new XYZ(box["max"]["x"].AsDouble() * scale, box["max"]["y"].AsDouble() * scale, box["max"]["z"].AsDouble(0) * scale);

            var sectionBox = new BoundingBoxXYZ
            {
                Min = min,
                Max = max
            };

            string viewName = name ?? (viewType + "-" + Guid.NewGuid().ToString("N").Substring(0, 4));
            foreach (View v in new FilteredElementCollector(document).OfClass(typeof(View)))
            {
                if (string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AgentException(ErrorCodes.InvalidArgument, "A view named '" + viewName + "' already exists.", true);
                }
            }

            ViewSection view = ViewSection.CreateSection(document, vft.Id, sectionBox);
            if (view == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "view.create_section produced no view.", true);
            }

            view.Name = viewName;

            return new OperationResult(
                operation.Id,
                "view.create_section",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(view) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["viewFamilyType"] = JsonValue.String(targetFamily.ToString()),
                    ["viewType"] = JsonValue.String(viewType),
                    ["viewName"] = JsonValue.String(viewName)
                }));
        }

        private static ViewFamilyType FindViewFamilyType(Document document, ViewFamily family)
        {
            foreach (ViewFamilyType vft in new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)))
            {
                if (vft.ViewFamily == family)
                {
                    return vft;
                }
            }

            throw new AgentException(ErrorCodes.TypeNotFound, "No ViewFamilyType found for " + family + ".", true);
        }
    }
}
