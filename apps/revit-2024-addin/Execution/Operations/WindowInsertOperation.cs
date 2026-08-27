using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>window.insert</c>: places a window family instance hosted by a wall.</summary>
    public static class WindowInsertOperation
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
            FamilySymbol symbol = TypeResolver.Resolve(document, args["type"], "window", policy, plan.Units).Value;

            if (symbol.Category == null || symbol.Category.Id.Value != (int)BuiltInCategory.OST_Windows)
            {
                throw new AgentException(ErrorCodes.CategoryMismatch,
                    "The resolved family is not in the Windows category.", true);
            }

            FamilyInstance instance = CreateInstance(document, host, symbol, args["location"], plan);
            if (instance == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "window.insert produced no instance.", true);
            }

            // Apply sill height if requested.
            double sillHeight = args["sillHeight"].AsDouble(0);
            if (sillHeight > 0)
            {
                double sillFeet = UnitNames.ToFeet(sillHeight, plan.Units);
                Parameter sillParam = instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                if (sillParam != null && !sillParam.IsReadOnly)
                {
                    sillParam.Set(sillFeet);
                }
            }

            return new OperationResult(
                operation.Id,
                "window.insert",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(instance) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["host"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(host.Id.Value),
                        ["name"] = JsonValue.String(host.Name)
                    }),
                    ["type"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["name"] = JsonValue.String(symbol.Name)
                    })
                }));
        }

        private static FamilyInstance CreateInstance(Document document, Wall host, FamilySymbol symbol, JsonValue location, AgentPlan plan)
        {
            string strategy = location["strategy"].AsString("wall_midpoint");
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
                    placement = curve.Evaluate(0.5, true);
                    break;

                case "offset_from_wall_start":
                {
                    double offset = location["offset"].AsDouble(0);
                    double t = Math.Min(1, offset * scale / curve.ApproximateLength);
                    placement = curve.Evaluate(t, true);
                    break;
                }

                case "offset_from_wall_end":
                {
                    double offset = location["offset"].AsDouble(0);
                    double t = Math.Max(0, 1 - offset * scale / curve.ApproximateLength);
                    placement = curve.Evaluate(t, true);
                    break;
                }

                case "explicit_point":
                {
                    JsonValue point = location["point"];
                    placement = new XYZ(
                        point["x"].AsDouble() * scale,
                        point["y"].AsDouble() * scale,
                        point["z"].AsDouble(0) * scale);
                    break;
                }

                default:
                    throw new AgentException(ErrorCodes.InvalidArgument,
                        "Unsupported location strategy '" + strategy + "'.", true);
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                document.Regenerate();
            }

            return document.Create.NewFamilyInstance(placement, symbol, host, StructuralType.NonStructural);
        }
    }
}
