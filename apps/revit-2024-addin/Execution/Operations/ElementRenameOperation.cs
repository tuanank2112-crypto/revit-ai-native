using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>element.rename</c>: changes the Name property of an element.</summary>
    public static class ElementRenameOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            Element target = ElementResolver.Resolve(document, operation.Args["target"], results);
            string newName = operation.Args["name"].AsString(null);

            if (string.IsNullOrEmpty(newName))
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "New name cannot be empty.", true);
            }

            target.Name = newName;

            return new OperationResult(
                operation.Id,
                "element.rename",
                OperationOutcome.Completed,
                modified: new[] { ElementResolver.Summarize(target) });
        }
    }
}
