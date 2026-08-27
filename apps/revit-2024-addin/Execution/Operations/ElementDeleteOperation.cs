using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>element.delete</c>: deletes a single element by reference.</summary>
    public static class ElementDeleteOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            Element target = ElementResolver.Resolve(document, operation.Args["target"], results);

            // Capture a before snapshot for the audit trail.
            ElementSummary before = ElementResolver.Summarize(target);

            // Delete the element. Revit handles dependent deletion automatically.
            document.Delete(target.Id);

            return new OperationResult(
                operation.Id,
                "element.delete",
                OperationOutcome.Completed,
                deleted: new[] { before });
        }
    }
}
