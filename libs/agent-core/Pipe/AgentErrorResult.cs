using System;

namespace AutodeskNativeAgent.Core.Pipe
{
    /// <summary>
    /// An exception carrying a stable <see cref="AutodeskNativeAgent.Core.Contracts.ErrorCodes"/>
    /// code, thrown by the pipe client. Mirrors the Revit add-in's AgentException shape so
    /// the MCP server can translate it without coupling core to the add-in assembly.
    /// </summary>
    public sealed class AgentErrorResult : Exception
    {
        /// <summary>Creates a pipe error result.</summary>
        public AgentErrorResult(string code, string message, bool recoverable = false, string suggestedAction = null)
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
