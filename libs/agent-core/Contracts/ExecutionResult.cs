using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>Terminal / transient states of a committed plan job, per execution-result.schema.json.</summary>
    public enum JobStatus
    {
        /// <summary>Accepted but not yet picked up.</summary>
        Queued = 0,

        /// <summary>Waiting for the Revit add-in to process it.</summary>
        WaitingForRevit = 1,

        /// <summary>Structural and argument validation in progress.</summary>
        Validating = 2,

        /// <summary>Awaiting a human confirmation inside Revit.</summary>
        AwaitingConfirmation = 3,

        /// <summary>Operations are being executed.</summary>
        Executing = 4,

        /// <summary>Post-execution assertions and measurements are running.</summary>
        Verifying = 5,

        /// <summary>All operations completed and verified.</summary>
        Completed = 6,

        /// <summary>A non-recoverable error occurred.</summary>
        Failed = 7,

        /// <summary>Cancelled by the user or an operator.</summary>
        Cancelled = 8,

        /// <summary>Changes were rolled back after a warning or validation failure.</summary>
        RolledBack = 9,

        /// <summary>Execution exceeded the allowed time budget.</summary>
        TimedOut = 10
    }

    /// <summary>Outcome of a single operation within an execution result.</summary>
    public enum OperationOutcome
    {
        /// <summary>Executed successfully.</summary>
        Completed = 0,

        /// <summary>Execution failed.</summary>
        Failed = 1,

        /// <summary>Not executed (dependency failed, or plan-level gate).</summary>
        Skipped = 2,

        /// <summary>Executed then undone by a rollback.</summary>
        RolledBack = 3
    }

    /// <summary>A stable, minimal description of an element created or affected.</summary>
    public sealed class ElementSummary
    {
        /// <summary>Creates an element summary.</summary>
        public ElementSummary(long elementId, string uniqueId, string category, string name = null, string typeName = null)
        {
            ElementId = elementId;
            UniqueId = uniqueId ?? string.Empty;
            Category = category ?? string.Empty;
            Name = name;
            TypeName = typeName;
        }

        /// <summary>Revit ElementId (long on the wire; Revit 2024 exposes int32, kept as long for the schema).</summary>
        public long ElementId { get; }

        /// <summary>Stable UniqueId.</summary>
        public string UniqueId { get; }

        /// <summary>Category display name, e.g. "Walls".</summary>
        public string Category { get; }

        /// <summary>Optional element name.</summary>
        public string Name { get; }

        /// <summary>Optional type name.</summary>
        public string TypeName { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["elementId"] = JsonValue.Number(ElementId),
                ["uniqueId"] = JsonValue.String(UniqueId),
                ["category"] = JsonValue.String(Category)
            };

            if (!string.IsNullOrEmpty(Name))
            {
                members["name"] = JsonValue.String(Name);
            }

            if (!string.IsNullOrEmpty(TypeName))
            {
                members["typeName"] = JsonValue.String(TypeName);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Parses an element summary from the wire form.</summary>
        public static ElementSummary FromJson(JsonValue json)
        {
            if (json == null || !json.IsObject)
            {
                return null;
            }

            return new ElementSummary(
                json["elementId"].AsLong(0),
                json["uniqueId"].AsString(string.Empty),
                json["category"].AsString(string.Empty),
                json["name"].AsString(null),
                json["typeName"].AsString(null));
        }
    }

    /// <summary>A requested-vs-actual measurement for an assertion or a verification.</summary>
    public sealed class Measurement
    {
        /// <summary>Creates a measurement.</summary>
        public Measurement(double requested, double actual, string unit, double difference, double tolerance, bool passed)
        {
            Requested = requested;
            Actual = actual;
            Unit = unit ?? string.Empty;
            Difference = difference;
            Tolerance = tolerance;
            Passed = passed;
        }

        /// <summary>Requested value (from the plan).</summary>
        public double Requested { get; }

        /// <summary>Measured value after execution.</summary>
        public double Actual { get; }

        /// <summary>Unit both values are expressed in.</summary>
        public string Unit { get; }

        /// <summary>Actual minus requested.</summary>
        public double Difference { get; }

        /// <summary>Allowed deviation.</summary>
        public double Tolerance { get; }

        /// <summary>True when <c>|difference| &lt;= tolerance</c>.</summary>
        public bool Passed { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["requested"] = JsonValue.Number(Requested),
                ["actual"] = JsonValue.Number(Actual),
                ["unit"] = JsonValue.String(Unit),
                ["difference"] = JsonValue.Number(Difference),
                ["tolerance"] = JsonValue.Number(Tolerance),
                ["passed"] = JsonValue.Bool(Passed)
            });
        }
    }

    /// <summary>Per-operation result entry in an <see cref="ExecutionResult"/>.</summary>
    public sealed class OperationResult
    {
        /// <summary>Creates an operation result.</summary>
        public OperationResult(
            string operationId,
            string operation,
            OperationOutcome status,
            IReadOnlyList<ElementSummary> created = null,
            IReadOnlyList<ElementSummary> modified = null,
            IReadOnlyList<ElementSummary> deleted = null,
            JsonValue resolved = null,
            IDictionary<string, JsonValue> measurements = null,
            IReadOnlyList<string> warnings = null,
            IReadOnlyList<string> errors = null)
        {
            OperationId = operationId ?? string.Empty;
            Operation = operation ?? string.Empty;
            Status = status;
            CreatedElements = created ?? System.Array.Empty<ElementSummary>();
            ModifiedElements = modified ?? System.Array.Empty<ElementSummary>();
            DeletedElements = deleted ?? System.Array.Empty<ElementSummary>();
            Resolved = resolved ?? JsonValue.Null;
            Measurements = measurements ?? new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            Warnings = warnings ?? System.Array.Empty<string>();
            Errors = errors ?? System.Array.Empty<string>();
        }

        /// <summary>Operation id from the plan.</summary>
        public string OperationId { get; }

        /// <summary>Operation name, e.g. wall.create.</summary>
        public string Operation { get; }

        /// <summary>Outcome.</summary>
        public OperationOutcome Status { get; }

        /// <summary>Elements created by this operation.</summary>
        public IReadOnlyList<ElementSummary> CreatedElements { get; }

        /// <summary>Elements modified by this operation.</summary>
        public IReadOnlyList<ElementSummary> ModifiedElements { get; }

        /// <summary>Elements deleted by this operation.</summary>
        public IReadOnlyList<ElementSummary> DeletedElements { get; }

        /// <summary>Resolved level/type/host descriptors, for the agent's benefit.</summary>
        public JsonValue Resolved { get; }

        /// <summary>Named measurements (e.g. "length") produced during verification.</summary>
        public IDictionary<string, JsonValue> Measurements { get; }

        /// <summary>Human-readable warnings from this operation.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Human-readable errors from this operation.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["operationId"] = JsonValue.String(OperationId),
                ["operation"] = JsonValue.String(Operation),
                ["status"] = JsonValue.String(StatusToWire(Status))
            };

            members["createdElements"] = JsonValue.Array(ToJsonArray(CreatedElements));
            members["modifiedElements"] = JsonValue.Array(ToJsonArray(ModifiedElements));
            members["deletedElements"] = JsonValue.Array(ToJsonArray(DeletedElements));

            if (!Resolved.IsNull)
            {
                members["resolved"] = Resolved;
            }

            if (Measurements.Count > 0)
            {
                members["measurements"] = JsonValue.Object(new Dictionary<string, JsonValue>(Measurements, StringComparer.Ordinal));
            }

            members["warnings"] = JsonValue.Array(ToJsonArray(Warnings));
            members["errors"] = JsonValue.Array(ToJsonArray(Errors));

            return JsonValue.Object(members);
        }

        private static List<JsonValue> ToJsonArray(IReadOnlyList<ElementSummary> items)
        {
            var list = new List<JsonValue>(items.Count);
            foreach (ElementSummary item in items)
            {
                list.Add(item.ToJson());
            }

            return list;
        }

        private static List<JsonValue> ToJsonArray(IReadOnlyList<string> items)
        {
            var list = new List<JsonValue>(items.Count);
            foreach (string item in items)
            {
                list.Add(JsonValue.String(item));
            }

            return list;
        }

        /// <summary>Maps an outcome to its wire token.</summary>
        public static string StatusToWire(OperationOutcome status)
        {
            switch (status)
            {
                case OperationOutcome.Failed: return "failed";
                case OperationOutcome.Skipped: return "skipped";
                case OperationOutcome.RolledBack: return "rolled_back";
                default: return "completed";
            }
        }
    }

    /// <summary>Outcome of a single assertion evaluation.</summary>
    public sealed class AssertionResult
    {
        /// <summary>Creates an assertion result.</summary>
        public AssertionResult(
            string kind,
            string target,
            JsonValue expected,
            JsonValue actual,
            double? difference,
            double? tolerance,
            bool passed)
        {
            Kind = kind ?? string.Empty;
            Target = target ?? string.Empty;
            Expected = expected ?? JsonValue.Null;
            Actual = actual ?? JsonValue.Null;
            Difference = difference;
            Tolerance = tolerance;
            Passed = passed;
        }

        /// <summary>Wire kind that was evaluated.</summary>
        public string Kind { get; }

        /// <summary>Reference the assertion targeted.</summary>
        public string Target { get; }

        /// <summary>Expected value.</summary>
        public JsonValue Expected { get; }

        /// <summary>Actual value observed.</summary>
        public JsonValue Actual { get; }

        /// <summary>Signed difference, when numeric.</summary>
        public double? Difference { get; }

        /// <summary>Allowed deviation, when numeric.</summary>
        public double? Tolerance { get; }

        /// <summary>Whether the check passed.</summary>
        public bool Passed { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["kind"] = JsonValue.String(Kind),
                ["target"] = JsonValue.String(Target),
                ["expected"] = Expected,
                ["actual"] = Actual,
                ["difference"] = Difference.HasValue ? JsonValue.Number(Difference.Value) : JsonValue.Null,
                ["tolerance"] = Tolerance.HasValue ? JsonValue.Number(Tolerance.Value) : JsonValue.Null,
                ["passed"] = JsonValue.Bool(Passed)
            };

            return JsonValue.Object(members);
        }
    }

    /// <summary>Rollback bookkeeping attached to a failed execution.</summary>
    public sealed class RollbackInfo
    {
        /// <summary>Creates rollback info.</summary>
        public RollbackInfo(bool performed, string reason = null, string restoredDocumentFingerprint = null)
        {
            Performed = performed;
            Reason = reason;
            RestoredDocumentFingerprint = restoredDocumentFingerprint;
        }

        /// <summary>Whether a rollback actually ran.</summary>
        public bool Performed { get; }

        /// <summary>Why the plan was rolled back, when applicable.</summary>
        public string Reason { get; }

        /// <summary>Fingerprint of the document after the rollback, when captured.</summary>
        public string RestoredDocumentFingerprint { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["performed"] = JsonValue.Bool(Performed)
            };

            if (!string.IsNullOrEmpty(Reason))
            {
                members["reason"] = JsonValue.String(Reason);
            }

            if (!string.IsNullOrEmpty(RestoredDocumentFingerprint))
            {
                members["restoredDocumentFingerprint"] = JsonValue.String(RestoredDocumentFingerprint);
            }

            return JsonValue.Object(members);
        }
    }

    /// <summary>
    /// Structured result of a committed plan, matching execution-result.schema.json.
    /// Immutable once created.
    /// </summary>
    public sealed class ExecutionResult
    {
        /// <summary>Creates an execution result.</summary>
        public ExecutionResult(
            string jobId,
            JobStatus status,
            string documentFingerprint,
            string planHash,
            DateTime? startedAtUtc = null,
            DateTime? completedAtUtc = null,
            bool atomic = true,
            IReadOnlyList<OperationResult> operations = null,
            IReadOnlyList<AssertionResult> assertions = null,
            RollbackInfo rollback = null,
            IReadOnlyList<AgentError> errors = null)
        {
            JobId = jobId ?? string.Empty;
            Status = status;
            DocumentFingerprint = documentFingerprint ?? string.Empty;
            PlanHash = planHash ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            Atomic = atomic;
            Operations = operations ?? System.Array.Empty<OperationResult>();
            Assertions = assertions ?? System.Array.Empty<AssertionResult>();
            Rollback = rollback;
            Errors = errors ?? System.Array.Empty<AgentError>();
        }

        /// <summary>Server-assigned job id.</summary>
        public string JobId { get; }

        /// <summary>Current job status.</summary>
        public JobStatus Status { get; }

        /// <summary>Fingerprint of the document at completion/failure time.</summary>
        public string DocumentFingerprint { get; }

        /// <summary>Canonical hash of the plan that produced this result.</summary>
        public string PlanHash { get; }

        /// <summary>When execution started.</summary>
        public DateTime? StartedAtUtc { get; }

        /// <summary>When a terminal state was reached.</summary>
        public DateTime? CompletedAtUtc { get; }

        /// <summary>Whether the plan ran inside a single transaction group.</summary>
        public bool Atomic { get; }

        /// <summary>Per-operation outcomes.</summary>
        public IReadOnlyList<OperationResult> Operations { get; }

        /// <summary>Assertion outcomes.</summary>
        public IReadOnlyList<AssertionResult> Assertions { get; }

        /// <summary>Rollback details, when a rollback happened.</summary>
        public RollbackInfo Rollback { get; }

        /// <summary>Plan-level errors.</summary>
        public IReadOnlyList<AgentError> Errors { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["jobId"] = JsonValue.String(JobId),
                ["status"] = JsonValue.String(StatusToWire(Status)),
                ["documentFingerprint"] = JsonValue.String(DocumentFingerprint),
                ["planHash"] = JsonValue.String(PlanHash),
                ["atomic"] = JsonValue.Bool(Atomic)
            };

            if (StartedAtUtc.HasValue)
            {
                members["startedAtUtc"] = JsonValue.String(StartedAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
            }

            if (CompletedAtUtc.HasValue)
            {
                members["completedAtUtc"] = JsonValue.String(CompletedAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
            }

            var ops = new List<JsonValue>(Operations.Count);
            foreach (OperationResult operation in Operations)
            {
                ops.Add(operation.ToJson());
            }

            members["operations"] = JsonValue.Array(ops);

            var asserts = new List<JsonValue>(Assertions.Count);
            foreach (AssertionResult assertion in Assertions)
            {
                asserts.Add(assertion.ToJson());
            }

            members["assertions"] = JsonValue.Array(asserts);

            if (Rollback != null)
            {
                members["rollback"] = Rollback.ToJson();
            }

            var errors = new List<JsonValue>(Errors.Count);
            foreach (AgentError error in Errors)
            {
                errors.Add(error.ToJson());
            }

            members["errors"] = JsonValue.Array(errors);

            return JsonValue.Object(members);
        }

        /// <summary>Maps a job status to its wire token.</summary>
        public static string StatusToWire(JobStatus status)
        {
            switch (status)
            {
                case JobStatus.WaitingForRevit: return "waiting_for_revit";
                case JobStatus.Validating: return "validating";
                case JobStatus.AwaitingConfirmation: return "awaiting_confirmation";
                case JobStatus.Executing: return "executing";
                case JobStatus.Verifying: return "verifying";
                case JobStatus.Completed: return "completed";
                case JobStatus.Failed: return "failed";
                case JobStatus.Cancelled: return "cancelled";
                case JobStatus.RolledBack: return "rolled_back";
                case JobStatus.TimedOut: return "timed_out";
                default: return "queued";
            }
        }
    }
}
