using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Validation
{
    /// <summary>Result of <see cref="PlanValidator"/>.</summary>
    public sealed class PlanValidationResult
    {
        /// <summary>Creates a validation result.</summary>
        public PlanValidationResult(bool valid, IReadOnlyList<AgentError> errors, int estimatedAffectedElements = 0)
        {
            Valid = valid;
            Errors = errors ?? System.Array.Empty<AgentError>();
            EstimatedAffectedElements = estimatedAffectedElements;
        }

        /// <summary>True when the plan passed every structural, allowlist and safety check.</summary>
        public bool Valid { get; }

        /// <summary>All problems found, or empty when <see cref="Valid"/>.</summary>
        public IReadOnlyList<AgentError> Errors { get; }

        /// <summary>Upper-bound estimate of elements the plan may touch, from the registry.</summary>
        public int EstimatedAffectedElements { get; }
    }

    /// <summary>
    /// Validates a plan before preview or execution. This is the gate that turns the
    /// machine-readable contract into an enforceable allowlist:
    /// duplicate ids, unknown/cyclic dependencies, operation allowlist membership,
    /// per-operation argument schema, and the plan safety ceiling are all checked here.
    /// </summary>
    public static class PlanValidator
    {
        /// <summary>Validates a plan against the registry and its own safety envelope.</summary>
        public static PlanValidationResult Validate(AgentPlan plan, CommandRegistry registry)
        {
            if (plan == null)
            {
                return new PlanValidationResult(
                    false,
                    new[] { new AgentError(ErrorCodes.InvalidArgument, "Plan is null.", true) });
            }

            if (registry == null)
            {
                return new PlanValidationResult(
                    false,
                    new[] { new AgentError(ErrorCodes.InternalError, "Command registry is null.", false) });
            }

            var errors = new List<AgentError>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int estimatedAffected = 0;

            if (plan.Operations.Count == 0)
            {
                errors.Add(new AgentError(
                    ErrorCodes.SchemaValidationFailed,
                    "A plan must contain at least one operation.",
                    true,
                    "Add one or more operations."));
                return new PlanValidationResult(false, errors);
            }

            if (plan.Operations.Count > 100)
            {
                errors.Add(new AgentError(
                    ErrorCodes.TooManyOperations,
                    "A plan may contain at most 100 operations.",
                    true));
            }

            foreach (PlanOperation operation in plan.Operations)
            {
                if (string.IsNullOrEmpty(operation.Id))
                {
                    errors.Add(new AgentError(
                        ErrorCodes.SchemaValidationFailed,
                        "Every operation requires a non-empty id.",
                        true));
                    continue;
                }

                if (!seenIds.Add(operation.Id))
                {
                    errors.Add(new AgentError(
                        ErrorCodes.DuplicateOperationId,
                        "Duplicate operation id '" + operation.Id + "'.",
                        true,
                        "Use a unique id per operation."));
                }

                if (string.IsNullOrEmpty(operation.Op))
                {
                    errors.Add(new AgentError(
                        ErrorCodes.SchemaValidationFailed,
                        "Operation '" + operation.Id + "' has no op name.",
                        true));
                    continue;
                }

                OperationDescriptor descriptor = registry.Find(operation.Op);
                if (descriptor == null)
                {
                    errors.Add(new AgentError(
                        ErrorCodes.UnknownOperation,
                        "Operation '" + operation.Op + "' is not in the command allowlist.",
                        true,
                        "Use one of: " + string.Join(", ", RegisteredNames(registry)) + "."));
                    continue;
                }

                List<string> argErrors = descriptor.ValidateArguments(operation.Args);
                if (argErrors.Count > 0)
                {
                    errors.Add(new AgentError(
                        ErrorCodes.InvalidArgument,
                        "Arguments of operation '" + operation.Id + "' (" + operation.Op + ") are invalid.",
                        true,
                        "Fix the argument errors.").With("details", JsonValue.String(string.Join("; ", argErrors))));
                }

                estimatedAffected += descriptor.MaxAffected;

                foreach (string dependency in operation.DependsOn)
                {
                    if (!seenIds.Contains(dependency))
                    {
                        errors.Add(new AgentError(
                            ErrorCodes.UnknownDependency,
                            "Operation '" + operation.Id + "' depends on unknown operation '" + dependency + "'.",
                            true));
                    }
                }
            }

            // Dependencies must form a DAG. Kahn's algorithm; the order is later derived
            // by the executor from the same edges. Note: the "seenIds" check above only
            // rejects forward references; cycles are detected here.
            var cycleError = DetectCycle(plan, seenIds);
            if (cycleError != null)
            {
                errors.Add(cycleError);
            }

            int ceiling = plan.Safety.MaximumElementsAffected;
            if (estimatedAffected > ceiling)
            {
                errors.Add(new AgentError(
                    ErrorCodes.AffectedElementLimitExceeded,
                    "The plan may affect up to " + estimatedAffected +
                    " elements, exceeding the safety ceiling of " + ceiling + ".",
                    true,
                    "Reduce the operations or raise safety.maximumElementsAffected."));
            }

            return new PlanValidationResult(errors.Count == 0, errors, estimatedAffected);
        }

        private static string[] RegisteredNames(CommandRegistry registry)
        {
            var names = new List<string>();
            foreach (OperationDescriptor d in registry.All)
            {
                names.Add(d.Op);
            }

            return names.ToArray();
        }

        private static AgentError DetectCycle(AgentPlan plan, ISet<string> ids)
        {
            var incoming = new Dictionary<string, int>(StringComparer.Ordinal);
            var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (PlanOperation operation in plan.Operations)
            {
                incoming[operation.Id] = 0;
                dependents[operation.Id] = new List<string>();
            }

            foreach (PlanOperation operation in plan.Operations)
            {
                foreach (string dependency in operation.DependsOn)
                {
                    if (!ids.Contains(dependency))
                    {
                        continue;
                    }

                    incoming[operation.Id]++;
                    dependents[dependency].Add(operation.Id);
                }
            }

            var queue = new Queue<string>();
            foreach (var pair in incoming)
            {
                if (pair.Value == 0)
                {
                    queue.Enqueue(pair.Key);
                }
            }

            int visited = 0;
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                visited++;
                foreach (string dependent in dependents[id])
                {
                    incoming[dependent]--;
                    if (incoming[dependent] == 0)
                    {
                        queue.Enqueue(dependent);
                    }
                }
            }

            if (visited != plan.Operations.Count)
            {
                var remaining = new List<string>();
                foreach (var pair in incoming)
                {
                    if (pair.Value > 0)
                    {
                        remaining.Add(pair.Key);
                    }
                }

                return new AgentError(
                    ErrorCodes.DependencyCycle,
                    "The plan dependency graph contains a cycle involving: " + string.Join(", ", remaining) + ".",
                    true,
                    "Remove the cyclic dependsOn edges.").With("operationIds", JsonValue.String(string.Join(",", remaining)));
            }

            return null;
        }
    }
}
