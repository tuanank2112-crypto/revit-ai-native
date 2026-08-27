using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>
    /// Stable, machine-readable error codes shared by the MCP server and the Revit runtime.
    /// </summary>
    /// <remarks>
    /// These strings are part of the public contract. Agents branch on them, so they must
    /// never be reworded. Human-readable explanation belongs in the message, not the code.
    /// </remarks>
    public static class ErrorCodes
    {
        // --- Connection / protocol -------------------------------------------------
        public const string RevitNotConnected = "REVIT_NOT_CONNECTED";
        public const string ProtocolVersionMismatch = "PROTOCOL_VERSION_MISMATCH";
        public const string MessageTooLarge = "MESSAGE_TOO_LARGE";
        public const string RequestTimeout = "REQUEST_TIMEOUT";
        public const string PipeError = "PIPE_ERROR";
        public const string MalformedMessage = "MALFORMED_MESSAGE";

        // --- Document state --------------------------------------------------------
        public const string NoActiveDocument = "NO_ACTIVE_DOCUMENT";
        public const string DocumentReadOnly = "DOCUMENT_READ_ONLY";
        public const string DocumentChangedSincePreview = "DOCUMENT_CHANGED_SINCE_PREVIEW";
        public const string ModalDialogBlocking = "MODAL_DIALOG_BLOCKING";
        public const string TransactionNotPermitted = "TRANSACTION_NOT_PERMITTED";

        // --- Plan validation -------------------------------------------------------
        public const string SchemaValidationFailed = "SCHEMA_VALIDATION_FAILED";
        public const string UnknownOperation = "UNKNOWN_OPERATION";
        public const string OperationNotSupported = "OPERATION_NOT_SUPPORTED";
        public const string UnitAmbiguous = "UNIT_AMBIGUOUS";
        public const string UnitUnsupported = "UNIT_UNSUPPORTED";
        public const string CoordinateSystemUnsupported = "COORDINATE_SYSTEM_UNSUPPORTED";
        public const string DependencyCycle = "DEPENDENCY_CYCLE";
        public const string UnknownDependency = "UNKNOWN_DEPENDENCY";
        public const string DuplicateOperationId = "DUPLICATE_OPERATION_ID";
        public const string UnresolvedReference = "UNRESOLVED_REFERENCE";
        public const string TooManyOperations = "TOO_MANY_OPERATIONS";
        public const string InvalidArgument = "INVALID_ARGUMENT";
        public const string MissingArgument = "MISSING_ARGUMENT";

        // --- Resolution ------------------------------------------------------------
        public const string AmbiguousType = "AMBIGUOUS_TYPE";
        public const string TypeNotFound = "TYPE_NOT_FOUND";
        public const string LevelNotFound = "LEVEL_NOT_FOUND";
        public const string AmbiguousLevel = "AMBIGUOUS_LEVEL";
        public const string AmbiguousParameter = "AMBIGUOUS_PARAMETER";
        public const string ParameterNotFound = "PARAMETER_NOT_FOUND";
        public const string ParameterReadOnly = "PARAMETER_READ_ONLY";
        public const string ParameterTypeMismatch = "PARAMETER_TYPE_MISMATCH";
        public const string HardDefaultNotAllowed = "HARD_DEFAULT_NOT_ALLOWED";

        // --- Element references ----------------------------------------------------
        public const string StaleElementReference = "STALE_ELEMENT_REFERENCE";
        public const string ElementNotFound = "ELEMENT_NOT_FOUND";
        public const string CategoryMismatch = "CATEGORY_MISMATCH";
        public const string InvalidHost = "INVALID_HOST";

        // --- Safety ----------------------------------------------------------------
        public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
        public const string ConfirmationTokenInvalid = "CONFIRMATION_TOKEN_INVALID";
        public const string ConfirmationTokenExpired = "CONFIRMATION_TOKEN_EXPIRED";
        public const string ConfirmationTokenAlreadyUsed = "CONFIRMATION_TOKEN_ALREADY_USED";
        public const string AffectedElementLimitExceeded = "AFFECTED_ELEMENT_LIMIT_EXCEEDED";
        public const string QueryLimitExceeded = "QUERY_LIMIT_EXCEEDED";
        public const string PathNotAllowed = "PATH_NOT_ALLOWED";
        public const string FileExists = "FILE_EXISTS";
        public const string FileAlreadyExists = "FILE_ALREADY_EXISTS";
        public const string RateLimited = "RATE_LIMITED";
        public const string PreviewRequired = "PREVIEW_REQUIRED";
        public const string PlanHashMismatch = "PLAN_HASH_MISMATCH";

        // --- Execution -------------------------------------------------------------
        public const string ExecutionFailed = "EXECUTION_FAILED";
        public const string VerificationFailed = "VERIFICATION_FAILED";
        public const string AssertionFailed = "ASSERTION_FAILED";
        public const string RevitApiError = "REVIT_API_ERROR";
        public const string RolledBack = "ROLLED_BACK";
        public const string Cancelled = "CANCELLED";
        public const string JobNotFound = "JOB_NOT_FOUND";
        public const string JobNotCancellable = "JOB_NOT_CANCELLABLE";
        public const string RollbackNotPossible = "ROLLBACK_NOT_POSSIBLE";
        public const string InternalError = "INTERNAL_ERROR";
    }

    /// <summary>
    /// A structured error. Every failure crossing a process or tool boundary is expressed
    /// as one of these; bare exception text is never propagated to the agent.
    /// </summary>
    public sealed class AgentError
    {
        /// <summary>Creates a structured error.</summary>
        /// <param name="code">A stable code from <see cref="ErrorCodes"/>.</param>
        /// <param name="message">Human-readable explanation, safe to display.</param>
        /// <param name="recoverable">True when the agent can plausibly retry after adjusting input.</param>
        /// <param name="suggestedAction">Concrete next step, e.g. "Set plan.units to mm".</param>
        public AgentError(string code, string message, bool recoverable = false, string suggestedAction = null)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("An error code is required.", nameof(code));
            }

            Code = code;
            Message = message ?? string.Empty;
            Recoverable = recoverable;
            SuggestedAction = suggestedAction;
            Details = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
        }

        /// <summary>Stable machine-readable code.</summary>
        public string Code { get; }

        /// <summary>Human-readable explanation.</summary>
        public string Message { get; }

        /// <summary>True when adjusting the request could succeed.</summary>
        public bool Recoverable { get; }

        /// <summary>Concrete remediation hint, when one exists.</summary>
        public string SuggestedAction { get; }

        /// <summary>Structured, already-sanitised supporting data.</summary>
        public IDictionary<string, JsonValue> Details { get; }

        /// <summary>Adds a detail entry and returns this instance for chaining.</summary>
        public AgentError With(string key, JsonValue value)
        {
            if (!string.IsNullOrEmpty(key) && value != null)
            {
                Details[key] = value;
            }

            return this;
        }

        /// <summary>Adds a string detail entry and returns this instance for chaining.</summary>
        public AgentError With(string key, string value)
        {
            if (value != null)
            {
                return With(key, JsonValue.String(value));
            }

            return this;
        }

        /// <summary>Adds a numeric detail entry and returns this instance for chaining.</summary>
        public AgentError With(string key, double value) => With(key, JsonValue.Number(value));

        /// <summary>Serialises to the wire format defined by pipe-envelope.schema.json.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["code"] = JsonValue.String(Code),
                ["message"] = JsonValue.String(Message),
                ["recoverable"] = JsonValue.Bool(Recoverable)
            };

            if (Details.Count > 0)
            {
                members["details"] = JsonValue.Object(new Dictionary<string, JsonValue>(Details, StringComparer.Ordinal));
            }

            if (!string.IsNullOrEmpty(SuggestedAction))
            {
                members["suggestedAction"] = JsonValue.String(SuggestedAction);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Reconstructs an error from its wire form. Returns null when <paramref name="json"/> is not an object.</summary>
        public static AgentError FromJson(JsonValue json)
        {
            if (json == null || !json.IsObject)
            {
                return null;
            }

            string code = json["code"].AsString(ErrorCodes.InternalError);
            string message = json["message"].AsString(string.Empty);
            bool recoverable = json["recoverable"].AsBool(false);
            string suggested = json["suggestedAction"].AsString(null);

            var error = new AgentError(code, message, recoverable, suggested);

            JsonValue details = json["details"];
            if (details.IsObject)
            {
                foreach (var member in details.Members)
                {
                    error.Details[member.Key] = member.Value;
                }
            }

            return error;
        }

        /// <summary>Returns "CODE: message", for logs.</summary>
        public override string ToString() => Code + ": " + Message;
    }
}
