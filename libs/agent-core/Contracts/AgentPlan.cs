using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>How the target document is selected.</summary>
    public enum DocumentStrategy
    {
        /// <summary>Use whichever document is active in Revit.</summary>
        ActiveDocument = 0
    }

    /// <summary>Whether a plan is being previewed or committed.</summary>
    public enum ExecutionMode
    {
        /// <summary>Analyse only; never mutate the model.</summary>
        Preview = 0,

        /// <summary>Execute inside a transaction group.</summary>
        Commit = 1
    }

    /// <summary>The coordinate system that plan geometry is expressed in.</summary>
    public enum CoordinateSystem
    {
        /// <summary>Revit internal (decimal feet), no transform applied.</summary>
        Internal = 0,

        /// <summary>Project coordinates.</summary>
        Project = 1,

        /// <summary>Shared (site) coordinates.</summary>
        Shared = 2,

        /// <summary>Relative to the active view.</summary>
        ActiveView = 3
    }

    /// <summary>Identifies which document a plan expects to run against.</summary>
    public sealed class DocumentTarget
    {
        /// <summary>Creates a document target.</summary>
        public DocumentTarget(
            DocumentStrategy strategy,
            string expectedTitle = null,
            string expectedPath = null,
            string expectedFingerprint = null)
        {
            Strategy = strategy;
            ExpectedTitle = expectedTitle;
            ExpectedPath = expectedPath;
            ExpectedFingerprint = expectedFingerprint;
        }

        /// <summary>Selection strategy.</summary>
        public DocumentStrategy Strategy { get; }

        /// <summary>Optional expected title; mismatch is reported, not silently ignored.</summary>
        public string ExpectedTitle { get; }

        /// <summary>Optional expected path.</summary>
        public string ExpectedPath { get; }

        /// <summary>
        /// Optional fingerprint captured during preview. When present, commit refuses to
        /// proceed against a different document with <c>DOCUMENT_CHANGED_SINCE_PREVIEW</c>.
        /// </summary>
        public string ExpectedFingerprint { get; }

        /// <summary>Parses from the wire form.</summary>
        public static DocumentTarget FromJson(JsonValue json)
        {
            // Only one strategy exists today; the schema constrains the value, and an
            // unrecognised string is rejected there rather than defaulted here.
            return new DocumentTarget(
                DocumentStrategy.ActiveDocument,
                json["expectedTitle"].AsString(null),
                json["expectedPath"].AsString(null),
                json["expectedFingerprint"].AsString(null));
        }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["strategy"] = JsonValue.String("active_document")
            };

            if (ExpectedTitle != null)
            {
                members["expectedTitle"] = JsonValue.String(ExpectedTitle);
            }

            if (ExpectedPath != null)
            {
                members["expectedPath"] = JsonValue.String(ExpectedPath);
            }

            if (ExpectedFingerprint != null)
            {
                members["expectedFingerprint"] = JsonValue.String(ExpectedFingerprint);
            }

            return JsonValue.Object(members);
        }
    }

    /// <summary>Per-plan safety envelope. These are limits the runtime enforces, not hints.</summary>
    public sealed class SafetySettings
    {
        /// <summary>Creates a safety envelope.</summary>
        public SafetySettings(
            bool requireUserConfirmation,
            bool createBackupBeforeCommit,
            int maximumElementsAffected,
            bool rollbackOnWarning,
            bool rollbackOnValidationFailure)
        {
            RequireUserConfirmation = requireUserConfirmation;
            CreateBackupBeforeCommit = createBackupBeforeCommit;
            MaximumElementsAffected = maximumElementsAffected;
            RollbackOnWarning = rollbackOnWarning;
            RollbackOnValidationFailure = rollbackOnValidationFailure;
        }

        /// <summary>True when a human must approve inside Revit before commit.</summary>
        public bool RequireUserConfirmation { get; }

        /// <summary>True to save a backup copy before mutating.</summary>
        public bool CreateBackupBeforeCommit { get; }

        /// <summary>Upper bound on elements this plan may create, modify or delete.</summary>
        public int MaximumElementsAffected { get; }

        /// <summary>True to roll back when any warning is raised.</summary>
        public bool RollbackOnWarning { get; }

        /// <summary>True to roll back when post-execution validation fails.</summary>
        public bool RollbackOnValidationFailure { get; }

        /// <summary>Conservative defaults used when a caller omits the section entirely.</summary>
        public static SafetySettings Conservative =>
            new SafetySettings(true, false, 100, false, true);

        /// <summary>Parses from the wire form, falling back to conservative values per field.</summary>
        public static SafetySettings FromJson(JsonValue json)
        {
            return new SafetySettings(
                json["requireUserConfirmation"].AsBool(true),
                json["createBackupBeforeCommit"].AsBool(false),
                json["maximumElementsAffected"].AsInt(100),
                json["rollbackOnWarning"].AsBool(false),
                json["rollbackOnValidationFailure"].AsBool(true));
        }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["requireUserConfirmation"] = JsonValue.Bool(RequireUserConfirmation),
                ["createBackupBeforeCommit"] = JsonValue.Bool(CreateBackupBeforeCommit),
                ["maximumElementsAffected"] = JsonValue.Number(MaximumElementsAffected),
                ["rollbackOnWarning"] = JsonValue.Bool(RollbackOnWarning),
                ["rollbackOnValidationFailure"] = JsonValue.Bool(RollbackOnValidationFailure)
            });
        }
    }

    /// <summary>A single operation within a plan.</summary>
    public sealed class PlanOperation
    {
        /// <summary>Creates an operation.</summary>
        public PlanOperation(
            string id,
            string op,
            JsonValue args,
            IReadOnlyList<string> dependsOn = null,
            IReadOnlyList<Assertion> assertions = null,
            JsonValue metadata = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An operation id is required.", nameof(id));
            }

            if (string.IsNullOrEmpty(op))
            {
                throw new ArgumentException("An operation name is required.", nameof(op));
            }

            Id = id;
            Op = op;
            Args = args ?? JsonValue.EmptyObject();
            DependsOn = dependsOn ?? System.Array.Empty<string>();
            Assertions = assertions ?? System.Array.Empty<Assertion>();
            Metadata = metadata ?? JsonValue.Null;
        }

        /// <summary>Plan-unique identifier, referenced by <c>$result.&lt;id&gt;</c>.</summary>
        public string Id { get; }

        /// <summary>Registry operation name, e.g. <c>wall.create</c>.</summary>
        public string Op { get; }

        /// <summary>Raw arguments, validated against the per-operation schema.</summary>
        public JsonValue Args { get; }

        /// <summary>Ids of operations that must complete first.</summary>
        public IReadOnlyList<string> DependsOn { get; }

        /// <summary>Post-execution checks.</summary>
        public IReadOnlyList<Assertion> Assertions { get; }

        /// <summary>Non-functional metadata. Never interpreted as instructions.</summary>
        public JsonValue Metadata { get; }

        /// <summary>Parses from the wire form.</summary>
        public static PlanOperation FromJson(JsonValue json)
        {
            var dependsOn = new List<string>();
            foreach (JsonValue item in json["dependsOn"].Items)
            {
                string dep = item.AsString(null);
                if (!string.IsNullOrEmpty(dep))
                {
                    dependsOn.Add(dep);
                }
            }

            var assertions = new List<Assertion>();
            foreach (JsonValue item in json["assertions"].Items)
            {
                Assertion assertion = Assertion.FromJson(item);
                if (assertion != null)
                {
                    assertions.Add(assertion);
                }
            }

            return new PlanOperation(
                json["id"].AsString(string.Empty),
                json["op"].AsString(string.Empty),
                json["args"],
                dependsOn,
                assertions,
                json["metadata"]);
        }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["id"] = JsonValue.String(Id),
                ["op"] = JsonValue.String(Op),
                ["args"] = Args
            };

            var deps = new List<JsonValue>();
            foreach (string dep in DependsOn)
            {
                deps.Add(JsonValue.String(dep));
            }

            members["dependsOn"] = JsonValue.Array(deps);

            var asserts = new List<JsonValue>();
            foreach (Assertion assertion in Assertions)
            {
                asserts.Add(assertion.ToJson());
            }

            members["assertions"] = JsonValue.Array(asserts);

            if (!Metadata.IsNull)
            {
                members["metadata"] = Metadata;
            }

            return JsonValue.Object(members);
        }
    }

    /// <summary>
    /// A structured plan submitted by an agent. Instances are immutable; validation and
    /// resolution produce separate result objects rather than mutating the plan.
    /// </summary>
    public sealed class AgentPlan
    {
        /// <summary>The only schema version this build accepts.</summary>
        public const string CurrentSchemaVersion = "1.0";

        /// <summary>Creates a plan.</summary>
        public AgentPlan(
            string requestId,
            string description,
            DocumentTarget document,
            ExternalUnit units,
            CoordinateSystem coordinateSystem,
            ExecutionMode executionMode,
            IReadOnlyList<PlanOperation> operations,
            SafetySettings safety,
            string schemaVersion = CurrentSchemaVersion)
        {
            RequestId = requestId ?? string.Empty;
            Description = description ?? string.Empty;
            Document = document ?? new DocumentTarget(DocumentStrategy.ActiveDocument);
            Units = units;
            CoordinateSystemValue = coordinateSystem;
            ExecutionMode = executionMode;
            Operations = operations ?? System.Array.Empty<PlanOperation>();
            Safety = safety ?? SafetySettings.Conservative;
            SchemaVersion = schemaVersion ?? CurrentSchemaVersion;
        }

        /// <summary>Declared schema version.</summary>
        public string SchemaVersion { get; }

        /// <summary>Caller-supplied request identifier, echoed through logs and results.</summary>
        public string RequestId { get; }

        /// <summary>Human-readable intent, surfaced in the confirmation UI.</summary>
        public string Description { get; }

        /// <summary>Target document.</summary>
        public DocumentTarget Document { get; }

        /// <summary>The unit all bare numbers in this plan are expressed in.</summary>
        public ExternalUnit Units { get; }

        /// <summary>Coordinate system for geometry in this plan.</summary>
        public CoordinateSystem CoordinateSystemValue { get; }

        /// <summary>Preview or commit.</summary>
        public ExecutionMode ExecutionMode { get; }

        /// <summary>Operations, in declaration order. Execution order comes from the dependency graph.</summary>
        public IReadOnlyList<PlanOperation> Operations { get; }

        /// <summary>Safety envelope.</summary>
        public SafetySettings Safety { get; }

        /// <summary>
        /// Parses a plan from JSON. Structural validity is the validator's job; this
        /// method only maps fields and reports values it cannot interpret at all.
        /// </summary>
        /// <param name="json">The plan document.</param>
        /// <param name="error">Set when the plan cannot be mapped.</param>
        /// <returns>The plan, or null when <paramref name="error"/> is set.</returns>
        public static AgentPlan FromJson(JsonValue json, out AgentError error)
        {
            error = null;

            if (json == null || !json.IsObject)
            {
                error = new AgentError(
                    ErrorCodes.SchemaValidationFailed,
                    "The plan must be a JSON object.",
                    true,
                    "Send an object conforming to agent-plan.schema.json.");
                return null;
            }

            string schemaVersion = json["schemaVersion"].AsString(null);
            if (schemaVersion != CurrentSchemaVersion)
            {
                error = new AgentError(
                    ErrorCodes.SchemaValidationFailed,
                    "Unsupported plan schemaVersion '" + (schemaVersion ?? "<missing>") + "'.",
                    true,
                    "Set schemaVersion to \"" + CurrentSchemaVersion + "\".")
                    .With("expected", CurrentSchemaVersion);
                return null;
            }

            // Units are mandatory and never inferred: a wrong guess here silently
            // produces geometry off by a factor of 25.4 or 1000.
            string unitText = json["units"].AsString(null);
            if (string.IsNullOrEmpty(unitText))
            {
                error = new AgentError(
                    ErrorCodes.UnitAmbiguous,
                    "Drawing units were not specified.",
                    true,
                    "Set plan.units to one of mm, cm, m, inch, ft.");
                return null;
            }

            ExternalUnit units;
            if (!UnitNames.TryParseLength(unitText, out units))
            {
                error = new AgentError(
                    ErrorCodes.UnitUnsupported,
                    "Unsupported unit '" + unitText + "'.",
                    true,
                    "Use one of mm, cm, m, inch, ft.")
                    .With("received", unitText);
                return null;
            }

            string csText = json["coordinateSystem"].AsString(null);
            CoordinateSystem coordinateSystem;
            if (!TryParseCoordinateSystem(csText, out coordinateSystem))
            {
                error = new AgentError(
                    ErrorCodes.CoordinateSystemUnsupported,
                    "Unsupported or missing coordinateSystem '" + (csText ?? "<missing>") + "'.",
                    true,
                    "Use one of internal, project, shared, active_view.");
                return null;
            }

            string modeText = json["executionMode"].AsString(null);
            ExecutionMode mode;
            if (modeText == "preview")
            {
                mode = ExecutionMode.Preview;
            }
            else if (modeText == "commit")
            {
                mode = ExecutionMode.Commit;
            }
            else
            {
                error = new AgentError(
                    ErrorCodes.SchemaValidationFailed,
                    "executionMode must be 'preview' or 'commit'.",
                    true,
                    "Set executionMode explicitly.");
                return null;
            }

            JsonValue opsJson = json["operations"];
            if (!opsJson.IsArray || opsJson.Count == 0)
            {
                error = new AgentError(
                    ErrorCodes.SchemaValidationFailed,
                    "A plan must contain at least one operation.",
                    true,
                    "Add one or more operations.");
                return null;
            }

            var operations = new List<PlanOperation>(opsJson.Count);
            foreach (JsonValue opJson in opsJson.Items)
            {
                string id = opJson["id"].AsString(null);
                string op = opJson["op"].AsString(null);

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(op))
                {
                    error = new AgentError(
                        ErrorCodes.SchemaValidationFailed,
                        "Every operation requires a non-empty 'id' and 'op'.",
                        true,
                        "Add the missing field.");
                    return null;
                }

                operations.Add(PlanOperation.FromJson(opJson));
            }

            return new AgentPlan(
                json["requestId"].AsString(string.Empty),
                json["description"].AsString(string.Empty),
                DocumentTarget.FromJson(json["document"]),
                units,
                coordinateSystem,
                mode,
                operations,
                SafetySettings.FromJson(json["safety"]),
                schemaVersion);
        }

        /// <summary>Serialises to the wire form. Round-trips through <see cref="FromJson"/>.</summary>
        public JsonValue ToJson()
        {
            var ops = new List<JsonValue>(Operations.Count);
            foreach (PlanOperation operation in Operations)
            {
                ops.Add(operation.ToJson());
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = JsonValue.String(SchemaVersion),
                ["requestId"] = JsonValue.String(RequestId),
                ["description"] = JsonValue.String(Description),
                ["document"] = Document.ToJson(),
                ["units"] = JsonValue.String(UnitNames.ToWire(Units)),
                ["coordinateSystem"] = JsonValue.String(CoordinateSystemToWire(CoordinateSystemValue)),
                ["executionMode"] = JsonValue.String(ExecutionMode == ExecutionMode.Commit ? "commit" : "preview"),
                ["operations"] = JsonValue.Array(ops),
                ["safety"] = Safety.ToJson()
            });
        }

        /// <summary>Finds an operation by id, or null.</summary>
        public PlanOperation FindOperation(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (PlanOperation operation in Operations)
            {
                if (string.Equals(operation.Id, id, StringComparison.Ordinal))
                {
                    return operation;
                }
            }

            return null;
        }

        private static bool TryParseCoordinateSystem(string text, out CoordinateSystem value)
        {
            switch (text)
            {
                case "internal":
                    value = CoordinateSystem.Internal;
                    return true;
                case "project":
                    value = CoordinateSystem.Project;
                    return true;
                case "shared":
                    value = CoordinateSystem.Shared;
                    return true;
                case "active_view":
                    value = CoordinateSystem.ActiveView;
                    return true;
                default:
                    value = CoordinateSystem.Project;
                    return false;
            }
        }

        /// <summary>Maps a coordinate system to its wire token.</summary>
        public static string CoordinateSystemToWire(CoordinateSystem value)
        {
            switch (value)
            {
                case CoordinateSystem.Internal: return "internal";
                case CoordinateSystem.Shared: return "shared";
                case CoordinateSystem.ActiveView: return "active_view";
                default: return "project";
            }
        }
    }
}
