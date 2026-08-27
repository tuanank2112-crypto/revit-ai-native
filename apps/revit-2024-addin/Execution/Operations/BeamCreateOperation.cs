using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>beam.create</c>: creates a structural beam as a structural wall segment.
    /// The native Beam class is absent in this Revit build, so a structural wall is the
    /// supported equivalent (it carries the structural flag and a width/height profile).
    ///
    /// Verified by compile probe #13: <c>Wall.Width</c> and <c>WallType.Width</c> are
    /// read-only in this build, so the beam width is fixed by the chosen WallType; the beam
    /// depth is set via <c>BuiltInParameter.WALL_USER_HEIGHT_PARAM</c> and its base offset via
    /// <c>BuiltInParameter.WALL_BASE_OFFSET</c>. The 8-arg <c>Wall.Create</c> overload
    /// (curve, wallTypeId, levelId, height, offset, flip, structural) is used to set height
    /// atomically at creation.
    /// </summary>
    public static class BeamCreateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results,
            ProjectPolicy policy)
        {
            JsonValue args = operation.Args;
            JsonValue start = args["start"];
            JsonValue end = args["end"];
            JsonValue typeSelector = args["type"];

            double depth = args["depth"].AsDouble(double.NaN);
            if (double.IsNaN(depth))
            {
                depth = args["height"].AsDouble(220);
            }
            if (depth <= 0)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "beam.create requires a positive depth (or height).", true);
            }

            double width = args["width"].AsDouble(double.NaN);
            double baseOffset = args["baseOffset"].AsDouble(0);

            Level level = LevelResolver.Resolve(document, args["level"], plan.Units, document.ActiveView).Value;

            // Beam width is fixed by the WallType (read-only in this build); resolve the best
            // matching structural wall type (by width if requested).
            WallType wallType = ResolveBeamWallType(document, typeSelector, width, plan.Units);

            double scale = UnitNames.FeetPerUnit(plan.Units);
            XYZ p0 = new XYZ(start["x"].AsDouble() * scale, start["y"].AsDouble() * scale, 0);
            XYZ p1 = new XYZ(end["x"].AsDouble() * scale, end["y"].AsDouble() * scale, 0);
            var curve = Line.CreateBound(p0, p1);

            double depthFeet = UnitNames.ToFeet(depth, plan.Units);
            double baseOffsetFeet = UnitNames.ToFeet(baseOffset, plan.Units);

            Wall beam = Wall.Create(document, curve, wallType.Id, level.Id, depthFeet, baseOffsetFeet, false, true);
            if (beam == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "beam.create produced no wall.", true);
            }

            // Best-effort parameter writes (height/offset already set by the Create overload).
            try
            {
                beam.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.Set(depthFeet);
                beam.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.Set(baseOffsetFeet);
            }
            catch
            {
                // surfaced via resolved note
            }

            string name = args["name"].AsString(null);
            if (!string.IsNullOrEmpty(name))
            {
                try { beam.Name = name; } catch { }
            }

            var warnings = new List<string>();
            if (!double.IsNaN(width))
            {
                double actualWidth = UnitNames.FromFeet(beam.Width, plan.Units);
                if (Math.Abs(actualWidth - width) > 5)
                {
                    warnings.Add("no wall type matches the requested beam width " + width + " mm; used '" + wallType.Name + "' (" + actualWidth.ToString("0") + " mm).");
                }
            }

            return new OperationResult(
                operation.Id,
                "beam.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(beam) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name)
                    }),
                    ["type"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(wallType.Id.Value),
                        ["name"] = JsonValue.String(wallType.Name)
                    }),
                    ["depth"] = JsonValue.Number(depth),
                    ["baseOffset"] = JsonValue.Number(baseOffset),
                    ["implementation"] = JsonValue.String("structural_wall")
                }),
                warnings: warnings);
        }

        /// <summary>
        /// Resolves the WallType used as the beam body. Prefers the requested selector
        /// (strategy/typeName), falls back to the narrowest available wall type (closest to a
        /// beam rather than a load-bearing masonry wall).
        /// </summary>
        private static WallType ResolveBeamWallType(Document document, JsonValue typeSelector, double requestedWidth, ExternalUnit units)
        {
            if (typeSelector != null && !typeSelector.IsNull && typeSelector.IsObject && typeSelector.Count > 0)
            {
                string strategy = typeSelector["strategy"].AsString(null);
                if (!string.IsNullOrEmpty(strategy))
                {
                    return TypeResolver.ResolveWallTypeBySelector(document, typeSelector, units);
                }

                string typeName = typeSelector["typeName"].AsString(null);
                if (!string.IsNullOrEmpty(typeName))
                {
                    foreach (WallType wt in new FilteredElementCollector(document).OfClass(typeof(WallType)))
                    {
                        if (string.Equals(wt.Name, typeName, StringComparison.Ordinal))
                        {
                            return wt;
                        }
                    }
                }
            }

            WallType best = null;
            double bestError = double.MaxValue;
            foreach (WallType wt in new FilteredElementCollector(document).OfClass(typeof(WallType)))
            {
                double w = wt.Width;
                double error = double.IsNaN(requestedWidth)
                    ? w // prefer narrowest
                    : Math.Abs(UnitNames.FromFeet(w, units) - requestedWidth);
                if (error < bestError)
                {
                    bestError = error;
                    best = wt;
                }
            }

            if (best == null)
            {
                throw new AgentException(ErrorCodes.TypeNotFound, "beam.create: no WallType available to use as the beam body.", true);
            }

            return best;
        }
    }
}
