using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>level.create</c>: creates a new level at the given elevation.</summary>
    public static class LevelCreateOperation
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
            double elevation = args["elevation"].AsDouble(double.NaN);
            if (double.IsNaN(elevation))
            {
                throw new AgentException(ErrorCodes.MissingArgument, "level.create requires 'elevation'.", true);
            }

            double elevationFeet = UnitNames.ToFeet(elevation, plan.Units);
            Level level = Level.Create(document, elevationFeet);
            if (level == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "level.create returned null.", false);
            }

            if (!string.IsNullOrEmpty(name))
            {
                level.Name = name;
            }

            return new OperationResult(
                operation.Id,
                "level.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(level) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name),
                        ["elevation"] = JsonValue.Number(UnitNames.FromFeet(level.Elevation, plan.Units))
                    })
                }));
        }
    }
}
