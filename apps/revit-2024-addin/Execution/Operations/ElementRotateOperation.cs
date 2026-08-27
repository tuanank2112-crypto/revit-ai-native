using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>element.rotate</c>: rotates an element around an axis by an angle.</summary>
    public static class ElementRotateOperation
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
                    "Element is pinned and cannot be rotated.", true);
            }

            JsonValue axisJson = operation.Args["axis"];
            double scale = UnitNames.FeetPerUnit(plan.Units);
            var origin = new XYZ(
                axisJson["origin"]["x"].AsDouble() * scale,
                axisJson["origin"]["y"].AsDouble() * scale,
                axisJson["origin"]["z"].AsDouble() * scale);
            var direction = new XYZ(
                axisJson["direction"]["x"].AsDouble(),
                axisJson["direction"]["y"].AsDouble(),
                axisJson["direction"]["z"].AsDouble()).Normalize();

            var axis = Line.CreateBound(origin, origin + direction);

            double angleDeg = operation.Args["angle"].AsDouble();
            string angleUnit = operation.Args["angleUnit"].AsString("deg");
            double angleRad = angleUnit == "rad" ? angleDeg : angleDeg * (Math.PI / 180d);

            ElementTransformUtils.RotateElement(document, target.Id, axis, angleRad);

            return new OperationResult(
                operation.Id,
                "element.rotate",
                OperationOutcome.Completed,
                modified: new[] { ElementResolver.Summarize(target) });
        }
    }
}
