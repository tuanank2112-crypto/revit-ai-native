using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Pipe
{
    /// <summary>The one protocol version this build speaks.</summary>
    public static class PipeProtocol
    {
        /// <summary>Current protocol version.</summary>
        public const string Version = "1.0";

        /// <summary>Default named pipe.</summary>
        public const string DefaultPipeName = "autodesk-native-agent";

        /// <summary>Maximum accepted message size (4 MiB).</summary>
        public const int MaxMessageBytes = 4 * 1024 * 1024;

        /// <summary>Message types from pipe-envelope.schema.json.</summary>
        public const string TypeRequest = "request";
        public const string TypeResponse = "response";
        public const string TypeHeartbeat = "heartbeat";
        public const string TypeHello = "hello";
        public const string TypeError = "error";

        /// <summary>Builds an envelope object per pipe-envelope.schema.json.</summary>
        public static JsonValue Envelope(string type, string requestId, string method, JsonValue payload, JsonValue data = null, AgentError error = null, string correlationId = null)
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["protocolVersion"] = JsonValue.String(Version),
                ["requestId"] = JsonValue.String(requestId ?? NewRequestId()),
                ["type"] = JsonValue.String(type),
                ["timestampUtc"] = JsonValue.String(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
            };

            if (!string.IsNullOrEmpty(correlationId))
            {
                members["correlationId"] = JsonValue.String(correlationId);
            }

            if (!string.IsNullOrEmpty(method))
            {
                members["method"] = JsonValue.String(method);
            }

            if (payload != null && !payload.IsNull)
            {
                members["payload"] = payload;
            }

            if (data != null && !data.IsNull)
            {
                members["data"] = data;
                members["success"] = JsonValue.Bool(true);
            }

            if (error != null)
            {
                members["error"] = error.ToJson();
                members["success"] = JsonValue.Bool(false);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Generates a request id.</summary>
        public static string NewRequestId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        /// <summary>
        /// Computes a user-scoped pipe name so two users on the same machine do not
        /// collide. The prefix "autodesk-native-agent-" is followed by the
        /// current Windows user name, lowercased and stripped of non-alphanumeric chars.
        /// </summary>
        public static string UserScopedPipeName()
        {
            string user = Environment.UserName;
            if (string.IsNullOrEmpty(user))
            {
                user = "default";
            }

            var sb = new System.Text.StringBuilder(user.Length + DefaultPipeName.Length + 2);
            foreach (char c in user)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            if (sb.Length == 0)
            {
                sb.Append("default");
            }

            return DefaultPipeName + "-" + sb;
        }

        /// <summary>Builds a hello envelope for the initial handshake.</summary>
        public static JsonValue Hello(string requestId, JsonValue payload)
        {
            return Envelope(TypeHello, requestId, "hello", payload);
        }

        /// <summary>Builds a heartbeat envelope.</summary>
        public static JsonValue Heartbeat(string requestId)
        {
            return Envelope(TypeHeartbeat, requestId, "heartbeat", null);
        }

        /// <summary>Builds a success response envelope.</summary>
        public static JsonValue Ok(string requestId, string method, JsonValue data, string correlationId = null)
        {
            return Envelope(TypeResponse, requestId, method, null, data, null, correlationId);
        }

        /// <summary>Builds an error response envelope.</summary>
        public static JsonValue Fail(string requestId, string method, AgentError error, string correlationId = null)
        {
            return Envelope(TypeResponse, requestId, method, null, null, error, correlationId);
        }

        /// <summary>Default heartbeat interval.</summary>
        public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

        /// <summary>Default request timeout.</summary>
        public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    }
}
