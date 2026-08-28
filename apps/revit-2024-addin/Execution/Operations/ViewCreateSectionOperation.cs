using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>view.create_section</c>: creates a section view through a rectangular box.
    /// When <c>viewType</c> is "elevation" the same op is reused (see PlanExecutor), but the
    /// creation path differs: <c>ViewSection.CreateSection</c> only accepts ViewFamily.Section
    /// types, so elevations go through <c>ElevationMarker.CreateElevationMarker</c> +
    /// <c>marker.CreateElevation(document, viewPlanId, index)</c> (both verified present in
    /// this build by reflection on RevitAPI.dll, 2026-08-27).
    ///
    /// Verified by compile probe #12: the section factory is
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

            if (targetFamily == ViewFamily.Elevation)
            {
                return ExecuteElevation(document, operation, plan, args, vft, min, viewName);
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

        /// <summary>
        /// Elevation creation path: <c>ViewSection.CreateSection</c> rejects non-Section
        /// ViewFamilyTypes ("The ViewFamilyType must be a Section ViewFamily"), so an
        /// <c>ElevationMarker</c> is placed at the box center (base Z) and one elevation view
        /// (direction index 0..3, optional <c>direction</c> arg) is generated from it against
        /// the plan view whose level is closest to the marker.
        /// </summary>
        private static OperationResult ExecuteElevation(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            JsonValue args,
            ViewFamilyType vft,
            XYZ min,
            string viewName)
        {
            JsonValue box = args["box"];
            double scale = UnitNames.FeetPerUnit(plan.Units);
            XYZ max = new XYZ(box["max"]["x"].AsDouble() * scale, box["max"]["y"].AsDouble() * scale, box["max"]["z"].AsDouble(0) * scale);
            XYZ origin = new XYZ((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, min.Z);

            int viewScale = (int)args["scale"].AsDouble(100);
            int direction = (int)args["direction"].AsDouble(0);
            direction = Math.Max(0, Math.Min(3, direction));

            ElevationMarker marker = ElevationMarker.CreateElevationMarker(document, vft.Id, origin, viewScale);
            ViewPlan planView = FindBestPlanView(document, origin.Z);

            ViewSection view;
            try
            {
                view = marker.CreateElevation(document, planView.Id, direction);
            }
            catch (Exception ex)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "view.create_elevation failed: " + ex.Message, true);
            }

            if (view == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "view.create_elevation produced no view.", true);
            }

            view.Name = viewName;

            return new OperationResult(
                operation.Id,
                "view.create_elevation",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(view) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["viewFamilyType"] = JsonValue.String("Elevation"),
                    ["viewType"] = JsonValue.String("elevation"),
                    ["viewName"] = JsonValue.String(viewName),
                    ["markerId"] = JsonValue.Number(marker.Id.Value),
                    ["planView"] = JsonValue.String(planView.Name),
                    ["direction"] = JsonValue.Number(direction)
                }));
        }

        /// <summary>Finds the non-template plan view whose level is closest to the marker Z.</summary>
        private static ViewPlan FindBestPlanView(Document document, double markerZFeet)
        {
            ViewPlan best = null;
            double bestDistance = double.MaxValue;
            foreach (ViewPlan vp in new FilteredElementCollector(document).OfClass(typeof(ViewPlan)))
            {
                if (vp.IsTemplate)
                {
                    continue;
                }

                Level level = vp.GenLevel;
                double distance = level != null ? Math.Abs(level.Elevation - markerZFeet) : double.MaxValue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = vp;
                }
            }

            if (best == null)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "view.create_elevation needs a floor/ceiling plan view in the document.", true);
            }

            return best;
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
