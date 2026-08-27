using System;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// An operation-level failure carrying a stable <see cref="AutodeskNativeAgent.Core.Contracts.ErrorCodes"/>
    /// code. The executor converts these into AgentError entries on the result envelope.
    /// </summary>
    public sealed class AgentException : Exception
    {
        /// <summary>Creates an agent exception.</summary>
        /// <param name="code">Stable error code from ErrorCodes.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="recoverable">Whether the agent can retry after adjusting input.</param>
        /// <param name="suggestedAction">Optional user-facing hint for fixing the error.</param>
        public AgentException(string code, string message, bool recoverable = false, string suggestedAction = null)
            : base(message)
        {
            Code = code;
            Recoverable = recoverable;
            SuggestedAction = suggestedAction;
        }

        /// <summary>Stable error code.</summary>
        public string Code { get; }

        /// <summary>Whether the agent can plausibly retry.</summary>
        public bool Recoverable { get; }

        /// <summary>Concrete remediation hint.</summary>
        public string SuggestedAction { get; }
    }
}
