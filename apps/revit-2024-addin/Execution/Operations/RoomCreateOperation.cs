using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>room.create</c>: creates a room on a level at a given UV point.</summary>
    public static class RoomCreateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            JsonValue levelSelector = args["level"];
            Level level = LevelResolver.Resolve(document, levelSelector, plan.Units, document.ActiveView).Value;

            // Check that we're in a plan view that supports room creation.
            View view = document.ActiveView;
            if (view == null || !(view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan))
            {
                throw new AgentException(ErrorCodes.InvalidArgument,
                    "Room creation requires an active floor plan or ceiling plan view.", true);
            }

            // Check for existing phases.
            PhaseArray phases = document.Phases;
            if (phases.IsEmpty)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "Document has no phases.", false);
            }

            JsonValue point = args["point"];
            if (point.IsNull)
            {
                throw new AgentException(ErrorCodes.InvalidArgument,
                    "room.create requires a point (u/v) to place the room.", true);
            }

            double u = UnitNames.ToFeet(point["u"].AsDouble(0), plan.Units);
            double v = UnitNames.ToFeet(point["v"].AsDouble(0), plan.Units);
            Room room = document.Create.NewRoom(level, new UV(u, v));
            if (room == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "Room creation returned null.", false);
            }

            if (!string.IsNullOrEmpty(args["name"].AsString(null)))
            {
                room.Name = args["name"].AsString();
            }

            return new OperationResult(
                operation.Id,
                "room.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(room) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name)
                    })
                }));
        }
    }
}
