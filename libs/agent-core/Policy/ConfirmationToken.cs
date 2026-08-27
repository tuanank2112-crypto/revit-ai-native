using System;
using System.Security.Cryptography;
using System.Text;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Policy
{
    /// <summary>State of a confirmation token.</summary>
    public enum ConfirmationState
    {
        /// <summary>Issued and awaiting a human decision.</summary>
        Pending = 0,

        /// <summary>Human accepted; the plan may commit.</summary>
        Accepted = 1,

        /// <summary>Human rejected; the plan must not run.</summary>
        Rejected = 2,

        /// <summary>Token expired before a decision was made.</summary>
        Expired = 3,

        /// <summary>Token was consumed (accepted or rejected).</summary>
        Used = 4
    }

    /// <summary>
    /// A single-use, time-limited, human-approval token. The plan hash it carries binds
    /// the approval to the exact plan that was previewed: committing a different plan with
    /// a valid token is refused with PLAN_HASH_MISMATCH.
    /// </summary>
    /// <remarks>
    /// The token value is a 128-bit cryptographic random, hex-encoded (32 chars), and the
    /// token is only ever accepted once. Tokens are created on the Revit side where the
    /// confirmation UI lives and consumed by the plan executor on the same side.
    /// </remarks>
    public sealed class ConfirmationToken
    {
        /// <summary>Default lifetime of a pending token.</summary>
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

        /// <summary>Creates a token. Token value is generated cryptographically.</summary>
        public ConfirmationToken(string jobId, string planHash, string description, TimeSpan? lifetime = null)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                throw new ArgumentException("A job id is required.", nameof(jobId));
            }

            if (string.IsNullOrEmpty(planHash))
            {
                throw new ArgumentException("A plan hash is required.", nameof(planHash));
            }

            JobId = jobId;
            PlanHash = planHash;
            Description = description ?? string.Empty;
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime ?? DefaultLifetime);
            Value = NewTokenValue();
            State = ConfirmationState.Pending;
        }

        /// <summary>Creates a token with a caller-supplied value (used by tests).</summary>
        public ConfirmationToken(string jobId, string planHash, string description, string tokenValue, TimeSpan? lifetime = null)
            : this(jobId, planHash, description, lifetime)
        {
            if (!string.IsNullOrEmpty(tokenValue))
            {
                Value = tokenValue;
            }
        }

        /// <summary>The opaque single-use value the agent must echo back.</summary>
        public string Value { get; }

        /// <summary>Job the token approves.</summary>
        public string JobId { get; }

        /// <summary>Canonical hash of the plan the token approves.</summary>
        public string PlanHash { get; }

        /// <summary>Human-readable description shown in the confirmation UI.</summary>
        public string Description { get; }

        /// <summary>When the token stops being valid.</summary>
        public DateTime ExpiresAtUtc { get; }

        /// <summary>Current state.</summary>
        public ConfirmationState State { get; private set; }

        /// <summary>When the state last changed.</summary>
        public DateTime? ChangedAtUtc { get; private set; }

        /// <summary>True when the token can still be used.</summary>
        public bool IsUsable => State == ConfirmationState.Pending && DateTime.UtcNow <= ExpiresAtUtc;

        /// <summary>
        /// Accepts the token. Returns a structured error when the token is no longer usable.
        /// </summary>
        public AgentError Accept()
        {
            if (State != ConfirmationState.Pending)
            {
                return new AgentError(
                    ErrorCodes.ConfirmationTokenAlreadyUsed,
                    "The confirmation token has already been " + StateText() + ".",
                    false,
                    "Request a new confirmation token.");
            }

            if (DateTime.UtcNow > ExpiresAtUtc)
            {
                State = ConfirmationState.Expired;
                ChangedAtUtc = DateTime.UtcNow;
                return new AgentError(
                    ErrorCodes.ConfirmationTokenExpired,
                    "The confirmation token has expired.",
                    false,
                    "Re-run the preview to obtain a fresh token.");
            }

            State = ConfirmationState.Accepted;
            ChangedAtUtc = DateTime.UtcNow;
            return null;
        }

        /// <summary>
        /// Rejects the token (human said no). The job must be cancelled.
        /// </summary>
        public void Reject()
        {
            if (State == ConfirmationState.Pending)
            {
                State = ConfirmationState.Rejected;
                ChangedAtUtc = DateTime.UtcNow;
            }
        }

        /// <summary>Marks the token consumed after a successful commit.</summary>
        public void MarkUsed()
        {
            if (State == ConfirmationState.Accepted)
            {
                State = ConfirmationState.Used;
                ChangedAtUtc = DateTime.UtcNow;
            }
        }

        /// <summary>Checks that a supplied token value matches this token and is still usable.</summary>
        public AgentError Validate(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) ||
                !string.Equals(candidate, Value, StringComparison.Ordinal))
            {
                return new AgentError(
                    ErrorCodes.ConfirmationTokenInvalid,
                    "The confirmation token value does not match.",
                    false,
                    "Use the token returned by the preview.");
            }

            if (State != ConfirmationState.Pending)
            {
                return new AgentError(
                    ErrorCodes.ConfirmationTokenAlreadyUsed,
                    "The confirmation token has already been " + StateText() + ".",
                    false,
                    "Request a new confirmation token.");
            }

            if (DateTime.UtcNow > ExpiresAtUtc)
            {
                State = ConfirmationState.Expired;
                ChangedAtUtc = DateTime.UtcNow;
                return new AgentError(
                    ErrorCodes.ConfirmationTokenExpired,
                    "The confirmation token has expired.",
                    false,
                    "Re-run the preview to obtain a fresh token.");
            }

            return null;
        }

        /// <summary>Serialises to the wire form (never includes the token value).</summary>
        public JsonValue ToJson()
        {
            return JsonValue.Object(new System.Collections.Generic.Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["jobId"] = JsonValue.String(JobId),
                ["planHash"] = JsonValue.String(PlanHash),
                ["description"] = JsonValue.String(Description),
                ["expiresAtUtc"] = JsonValue.String(ExpiresAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")),
                ["state"] = JsonValue.String(StateToWire(State))
            });
        }

        private string StateText()
        {
            switch (State)
            {
                case ConfirmationState.Accepted: return "accepted";
                case ConfirmationState.Rejected: return "rejected";
                case ConfirmationState.Expired: return "expired";
                case ConfirmationState.Used: return "used";
                default: return "pending";
            }
        }

        private static string StateToWire(ConfirmationState state)
        {
            switch (state)
            {
                case ConfirmationState.Accepted: return "accepted";
                case ConfirmationState.Rejected: return "rejected";
                case ConfirmationState.Expired: return "expired";
                case ConfirmationState.Used: return "used";
                default: return "pending";
            }
        }

        private static string NewTokenValue()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[16];
                rng.GetBytes(bytes);
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }
    }
}
