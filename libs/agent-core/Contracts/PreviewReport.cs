using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>Status of a single operation in a preview report.</summary>
    public enum PreviewStatus
    {
        /// <summary>Everything resolved; the operation can run.</summary>
        Ready = 0,

        /// <summary>Resolution failed (type/level/host); the operation cannot run as-is.</summary>
        Blocked = 1,

        /// <summary>Not evaluated (dependency blocked).</summary>
        Skipped = 2,

        /// <summary>A validation error was attached.</summary>
        Error = 3
    }

    /// <summary>Per-operation dry-run preview, matching preview-report.schema.json.</summary>
    public sealed class OperationPreview
    {
        /// <summary>Creates an operation preview.</summary>
        public OperationPreview(
            string operationId,
            string operation,
            PreviewStatus status,
            int willCreate = 0,
            int willModify = 0,
            int willDelete = 0,
            JsonValue resolved = null,
            IDictionary<string, JsonValue> measurements = null,
            IReadOnlyList<string> hardDefaultsUsed = null,
            IReadOnlyList<string> warnings = null,
            IReadOnlyList<AgentError> errors = null)
        {
            OperationId = operationId ?? string.Empty;
            Operation = operation ?? string.Empty;
            Status = status;
            WillCreate = willCreate;
            WillModify = willModify;
            WillDelete = willDelete;
            Resolved = resolved ?? JsonValue.Null;
            Measurements = measurements ?? new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            HardDefaultsUsed = hardDefaultsUsed ?? System.Array.Empty<string>();
            Warnings = warnings ?? System.Array.Empty<string>();
            Errors = errors ?? System.Array.Empty<AgentError>();
        }

        /// <summary>Operation id.</summary>
        public string OperationId { get; }

        /// <summary>Operation name.</summary>
        public string Operation { get; }

        /// <summary>Dry-run outcome for this operation.</summary>
        public PreviewStatus Status { get; }

        /// <summary>Estimated count of elements the operation would create.</summary>
        public int WillCreate { get; }

        /// <summary>Estimated count of elements the operation would modify.</summary>
        public int WillModify { get; }

        /// <summary>Estimated count of elements the operation would delete.</summary>
        public int WillDelete { get; }

        /// <summary>Resolved level/type/host descriptors.</summary>
        public JsonValue Resolved { get; }

        /// <summary>Planned measurements (requested values).</summary>
        public IDictionary<string, JsonValue> Measurements { get; }

        /// <summary>Defaults the operation would fall back to, if any.</summary>
        public IReadOnlyList<string> HardDefaultsUsed { get; }

        /// <summary>Warnings surfaced during preview.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Errors that blocked this operation.</summary>
        public IReadOnlyList<AgentError> Errors { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["operationId"] = JsonValue.String(OperationId),
                ["operation"] = JsonValue.String(Operation),
                ["status"] = JsonValue.String(StatusToWire(Status)),
                ["willCreate"] = JsonValue.Number(WillCreate),
                ["willModify"] = JsonValue.Number(WillModify),
                ["willDelete"] = JsonValue.Number(WillDelete)
            };

            if (!Resolved.IsNull)
            {
                members["resolved"] = Resolved;
            }

            if (Measurements.Count > 0)
            {
                members["measurements"] = JsonValue.Object(new Dictionary<string, JsonValue>(Measurements, StringComparer.Ordinal));
            }

            if (HardDefaultsUsed.Count > 0)
            {
                var defaults = new List<JsonValue>(HardDefaultsUsed.Count);
                foreach (string d in HardDefaultsUsed)
                {
                    defaults.Add(JsonValue.String(d));
                }

                members["hardDefaultsUsed"] = JsonValue.Array(defaults);
            }

            if (Warnings.Count > 0)
            {
                var warnings = new List<JsonValue>(Warnings.Count);
                foreach (string w in Warnings)
                {
                    warnings.Add(JsonValue.String(w));
                }

                members["warnings"] = JsonValue.Array(warnings);
            }

            if (Errors.Count > 0)
            {
                var errors = new List<JsonValue>(Errors.Count);
                foreach (AgentError e in Errors)
                {
                    errors.Add(e.ToJson());
                }

                members["errors"] = JsonValue.Array(errors);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Maps a preview status to its wire token.</summary>
        public static string StatusToWire(PreviewStatus status)
        {
            switch (status)
            {
                case PreviewStatus.Blocked: return "blocked";
                case PreviewStatus.Skipped: return "skipped";
                case PreviewStatus.Error: return "error";
                default: return "ready";
            }
        }
    }

    /// <summary>Summary counters of a preview report.</summary>
    public sealed class PreviewSummary
    {
        /// <summary>Creates a summary.</summary>
        public PreviewSummary(int willCreate, int willModify, int willDelete, int estimatedAffectedElements, bool requiresUserConfirmation)
        {
            WillCreate = willCreate;
            WillModify = willModify;
            WillDelete = willDelete;
            EstimatedAffectedElements = estimatedAffectedElements;
            RequiresUserConfirmation = requiresUserConfirmation;
        }

        /// <summary>Total elements the plan would create.</summary>
        public int WillCreate { get; }

        /// <summary>Total elements the plan would modify.</summary>
        public int WillModify { get; }

        /// <summary>Total elements the plan would delete.</summary>
        public int WillDelete { get; }

        /// <summary>Sum of the affected buckets; the number compared with the plan's safety ceiling.</summary>
        public int EstimatedAffectedElements { get; }

        /// <summary>True when the policy or the plan itself requires a human confirmation.</summary>
        public bool RequiresUserConfirmation { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["willCreate"] = JsonValue.Number(WillCreate),
                ["willModify"] = JsonValue.Number(WillModify),
                ["willDelete"] = JsonValue.Number(WillDelete),
                ["estimatedAffectedElements"] = JsonValue.Number(EstimatedAffectedElements),
                ["requiresUserConfirmation"] = JsonValue.Bool(RequiresUserConfirmation)
            });
        }
    }

    /// <summary>
    /// Dry-run report produced by a preview. Never mutates the model, matching
    /// preview-report.schema.json.
    /// </summary>
    public sealed class PreviewReport
    {
        /// <summary>Creates a preview report.</summary>
        public PreviewReport(
            string planHash,
            string documentFingerprint,
            IReadOnlyList<OperationPreview> operations,
            PreviewSummary summary,
            DateTime? createdAtUtc = null,
            IReadOnlyList<string> warnings = null,
            IReadOnlyList<AgentError> errors = null)
        {
            PlanHash = planHash ?? string.Empty;
            DocumentFingerprint = documentFingerprint ?? string.Empty;
            Operations = operations ?? System.Array.Empty<OperationPreview>();
            Summary = summary;
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
            Warnings = warnings ?? System.Array.Empty<string>();
            Errors = errors ?? System.Array.Empty<AgentError>();
        }

        /// <summary>Canonical hash of the plan.</summary>
        public string PlanHash { get; }

        /// <summary>Fingerprint of the document the preview ran against.</summary>
        public string DocumentFingerprint { get; }

        /// <summary>When the preview was produced.</summary>
        public DateTime CreatedAtUtc { get; }

        /// <summary>Per-operation previews.</summary>
        public IReadOnlyList<OperationPreview> Operations { get; }

        /// <summary>Plan-level warnings.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Plan-level errors.</summary>
        public IReadOnlyList<AgentError> Errors { get; }

        /// <summary>Aggregated counters.</summary>
        public PreviewSummary Summary { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["planHash"] = JsonValue.String(PlanHash),
                ["documentFingerprint"] = JsonValue.String(DocumentFingerprint),
                ["dryRun"] = JsonValue.Bool(true),
                ["createdAtUtc"] = JsonValue.String(CreatedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
            };

            var ops = new List<JsonValue>(Operations.Count);
            foreach (OperationPreview preview in Operations)
            {
                ops.Add(preview.ToJson());
            }

            members["operations"] = JsonValue.Array(ops);

            if (Warnings.Count > 0)
            {
                var warnings = new List<JsonValue>(Warnings.Count);
                foreach (string w in Warnings)
                {
                    warnings.Add(JsonValue.String(w));
                }

                members["warnings"] = JsonValue.Array(warnings);
            }

            if (Errors.Count > 0)
            {
                var errors = new List<JsonValue>(Errors.Count);
                foreach (AgentError e in Errors)
                {
                    errors.Add(e.ToJson());
                }

                members["errors"] = JsonValue.Array(errors);
            }

            members["summary"] = Summary.ToJson();

            return JsonValue.Object(members);
        }
    }
}
