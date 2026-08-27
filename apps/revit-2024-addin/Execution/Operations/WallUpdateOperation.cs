using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>wall.update</c>: modifies properties of an existing wall.</summary>
    public static class WallUpdateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            Wall wall = ElementResolver.ResolveWallHost(document, args["target"], results);
            ElementSummary before = ElementResolver.Summarize(wall);

            // Change type if requested.
            if (!args["type"].IsNull)
            {
                WallType newType = TypeResolver.ResolveWallType(document, args["type"]);
                wall.WallType = newType;
            }

            // Change height if requested.
            if (!args["height"].IsNull)
            {
                double height = args["height"].AsDouble();
                double heightFeet = UnitNames.ToFeet(height, plan.Units);
                Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (heightParam != null && !heightParam.IsReadOnly)
                {
                    heightParam.Set(heightFeet);
                }
            }

            // Change base offset.
            if (!args["baseOffset"].IsNull)
            {
                double offset = args["baseOffset"].AsDouble();
                double offsetFeet = UnitNames.ToFeet(offset, plan.Units);
                Parameter baseOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffsetParam != null && !baseOffsetParam.IsReadOnly)
                {
                    baseOffsetParam.Set(offsetFeet);
                }
            }

            // Change top offset.
            if (!args["topOffset"].IsNull)
            {
                double offset = args["topOffset"].AsDouble();
                offset = UnitNames.ToFeet(offset, plan.Units);
                Parameter topOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                if (topOffsetParam != null && !topOffsetParam.IsReadOnly)
                {
                    topOffsetParam.Set(offset);
                }
            }

            // Change structural flag.
            if (!args["structural"].IsNull)
            {
                bool structural = args["structural"].AsBool();
                Parameter structuralParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                if (structuralParam != null && !structuralParam.IsReadOnly)
                {
                    structuralParam.Set(structural ? 1 : 0);
                }
            }

            var beforeSummary = ElementResolver.Summarize(wall);
            var afterSummary = ElementResolver.Summarize(wall);

            return new OperationResult(
                operation.Id,
                "wall.update",
                OperationOutcome.Completed,
                modified: new[] { afterSummary },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["before"] = beforeSummary.ToJson(),
                    ["after"] = afterSummary.ToJson()
                }));
        }
    }
}
