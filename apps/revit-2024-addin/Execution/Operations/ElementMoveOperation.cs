using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>element.move</c>: translates an element by a vector in plan units.</summary>
    public static class ElementMoveOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            Element target = ElementResolver.Resolve(document, operation.Args["target"], results);

            if (target.Pinned)
            {
                throw new AgentException(ErrorCodes.InvalidArgument,
                    "Element is pinned and cannot be moved.", true,
                    "Unpin the element or use a different target.");
            }

            JsonValue vector = operation.Args["vector"];
            double scale = UnitNames.FeetPerUnit(plan.Units);
            var translation = new XYZ(
                vector["x"].AsDouble() * scale,
                vector["y"].AsDouble() * scale,
                vector["z"].AsDouble() * scale);

            ElementTransformUtils.MoveElement(document, target.Id, translation);

            return new OperationResult(
                operation.Id,
                "element.move",
                OperationOutcome.Completed,
                modified: new[] { ElementResolver.Summarize(target) });
        }
    }
}
