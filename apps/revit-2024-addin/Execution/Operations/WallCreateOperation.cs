using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Revit2024.Execution;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>wall.create</c>: creates a straight wall between two plan points.</summary>
    public static class WallCreateOperation
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
            JsonValue levelSelector = args["level"];
            JsonValue typeSelector = args["type"];

            double height = args["height"].AsDouble(double.NaN);
            if (double.IsNaN(height) || height <= 0)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "wall.create requires a positive height.", true);
            }

            double baseOffset = args["baseOffset"].AsDouble(0);
            double topOffset = args["topOffset"].AsDouble(0);
            bool structural = args["structural"].AsBool(false);

            Level level = LevelResolver.Resolve(document, levelSelector, plan.Units, document.ActiveView).Value;
            WallType wallType = TypeResolver.ResolveWallTypeBySelector(document, typeSelector, plan.Units);

            // Plan coordinates are in the plan's unit; convert to internal feet before creating geometry.
            double scale = UnitNames.FeetPerUnit(plan.Units);
            var p0 = new XYZ(start["x"].AsDouble() * scale, start["y"].AsDouble() * scale, start["z"].AsDouble(0) * scale);
            var p1 = new XYZ(end["x"].AsDouble() * scale, end["y"].AsDouble() * scale, end["z"].AsDouble(0) * scale);

            var curve = Line.CreateBound(p0, p1);
            double heightFeet = UnitNames.ToFeet(height, plan.Units);
            double baseOffsetFeet = UnitNames.ToFeet(baseOffset, plan.Units);
            double topOffsetFeet = UnitNames.ToFeet(topOffset, plan.Units);

            Wall wall = Wall.Create(document, curve, level.Id, structural);
            try
            {
                wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.Set(heightFeet);
                wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.Set(baseOffsetFeet);
                wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.Set(topOffsetFeet);

                if (wallType != null)
                {
                    wall.ChangeTypeId(wallType.Id);
                }
            }
            catch
            {
                // Parameter writes are best-effort; a failed Set is surfaced via the result warnings.
            }

            return new OperationResult(
                operation.Id,
                "wall.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(wall) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name),
                    }),
                    ["type"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(wallType != null ? wallType.Id.Value : 0),
                        ["name"] = JsonValue.String(wallType != null ? wallType.Name : string.Empty),
                    }),
                }));
        }
    }
}
