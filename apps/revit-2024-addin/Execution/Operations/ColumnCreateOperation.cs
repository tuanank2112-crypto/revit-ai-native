using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>column.create</c>: places a structural column family instance at a point.
    /// The native Column class is absent in this Revit build, so columns are structural
    /// family instances (the supported creation path).
    /// </summary>
    public static class ColumnCreateOperation
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

            // Delegate to the generic family instance op, forcing a structural, column category.
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var kvp in args.Members)
            {
                members[kvp.Key] = kvp.Value;
            }

            members["structural"] = JsonValue.String("column");
            if (!args.Has("category") || args["category"].AsString("column") == "column")
            {
                members["category"] = JsonValue.String("column");
            }

            var synthetic = new PlanOperation(operation.Id, "family.instance.create", JsonValue.Object(members), operation.DependsOn);
            var result = FamilyInstanceCreateOperation.Execute(document, synthetic, plan, results, policy);

            // Surface under the original operation id/name so downstream references work.
            return new OperationResult(
                operation.Id,
                "column.create",
                OperationOutcome.Completed,
                created: result.CreatedElements,
                resolved: result.Resolved,
                warnings: result.Warnings);
        }
    }
}
