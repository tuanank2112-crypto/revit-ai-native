using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Revit2024.Execution;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>door.insert</c>: places a door family instance hosted by a wall.</summary>
    public static class DoorInsertOperation
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

            Wall host = ElementResolver.ResolveWallHost(document, args["host"], results);
            FamilySymbol symbol = TypeResolver.Resolve(document, args["type"], "door", policy, plan.Units).Value;

            FamilyInstance instance = CreateInstance(document, host, symbol, args["location"], plan);
            if (instance == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "door.insert produced no instance.", true);
            }

            // Facing/hand flip requests.
            string facing = args["facing"].AsString("default");
            string hand = args["hand"].AsString("default");
            try
            {
                if (facing == "flip")
                {
                    instance.flipFacing();
                }

                if (hand == "flip")
                {
                    instance.flipHand();
                }
            }
            catch
            {
                // Flipping is best-effort; some families do not support it.
            }

            string levelName = string.Empty;
            try
            {
                Level level = document.GetElement(instance.LevelId) as Level;
                levelName = level != null ? level.Name : string.Empty;
            }
            catch
            {
                levelName = string.Empty;
            }

            return new OperationResult(
                operation.Id,
                "door.insert",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(instance) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["host"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(host.Id.Value),
                        ["name"] = JsonValue.String(host.Name),
                    }),
                    ["type"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["name"] = JsonValue.String(symbol.Name),
                    }),
                    ["level"] = JsonValue.String(levelName),
                }));
        }

        private static FamilyInstance CreateInstance(Document document, Wall host, FamilySymbol symbol, JsonValue location, AgentPlan plan)
        {
            string strategy = location["strategy"].AsString("wall_midpoint");

            // Convert plan length units to the host's local coordinate scale.
            double scale = UnitNames.FeetPerUnit(plan.Units);
            XYZ placement;

            LocationCurve locationCurve = host.Location as LocationCurve;
            if (locationCurve == null)
            {
                throw new AgentException(ErrorCodes.InvalidHost, "Host wall has no location curve.", true);
            }

            Curve curve = locationCurve.Curve;
            switch (strategy)
            {
                case "wall_midpoint":
                {
                    placement = curve.Evaluate(0.5, true);
                    break;
                }

                case "offset_from_wall_start":
                {
                    double offset = location["offset"].AsDouble(0);
                    double t = Math.Min(1, offset / curve.ApproximateLength);
                    placement = curve.Evaluate(t, true);
                    break;
                }

                case "offset_from_wall_end":
                {
                    double offset = location["offset"].AsDouble(0);
                    double t = Math.Max(0, 1 - offset / curve.ApproximateLength);
                    placement = curve.Evaluate(t, true);
                    break;
                }

                case "explicit_point":
                {
                    JsonValue point = location["point"];
                    placement = new XYZ(
                        point["x"].AsDouble() * UnitNames.FeetPerUnit(plan.Units),
                        point["y"].AsDouble() * UnitNames.FeetPerUnit(plan.Units),
                        point["z"].AsDouble(0) * UnitNames.FeetPerUnit(plan.Units));
                    break;
                }

                default:
                    throw new AgentException(ErrorCodes.InvalidArgument,
                        "Unsupported door location strategy '" + strategy + "'.", true);
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                document.Regenerate();
            }

            FamilyInstance instance = document.Create.NewFamilyInstance(placement, symbol, host, StructuralType.NonStructural);
            return instance;
        }
    }
}
