using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>grid.create</c>: creates a straight grid line between two XY points.</summary>
    public static class GridCreateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            JsonValue start = args["start"];
            JsonValue end = args["end"];

            double scale = UnitNames.FeetPerUnit(plan.Units);
            XYZ p1 = new XYZ(start["x"].AsDouble(0) * scale, start["y"].AsDouble(0) * scale, 0);
            XYZ p2 = new XYZ(end["x"].AsDouble(0) * scale, end["y"].AsDouble(0) * scale, 0);

            Grid grid = Grid.Create(document, Line.CreateBound(p1, p2));
            if (grid == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "grid.create returned null.", false);
            }

            string name = args["name"].AsString(null);
            if (!string.IsNullOrEmpty(name))
            {
                grid.Name = name;
            }

            return new OperationResult(
                operation.Id,
                "grid.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(grid) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["name"] = JsonValue.String(grid.Name)
                }));
        }
    }
}
