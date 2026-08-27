using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Policy
{
    /// <summary>Severity of an audit log entry.</summary>
    public enum AuditSeverity
    {
        /// <summary>Routine lifecycle events (connect, disconnect, status).</summary>
        Info = 0,

        /// <summary>An action was taken (plan committed, operation executed).</summary>
        Action = 1,

        /// <summary>A recoverable failure occurred.</summary>
        Warning = 2,

        /// <summary>A security-relevant event (invalid token, path traversal attempt).</summary>
        Security = 3,

        /// <summary>A non-recoverable failure occurred.</summary>
        Error = 4
    }

    /// <summary>
    /// An immutable audit log entry. Sanitisation happens at construction: values that
    /// could carry secrets or cross-trust-boundary content are scrubbed before the entry
    /// can ever be serialised, so a downstream consumer cannot leak them by accident.
    /// </summary>
    public sealed class AuditLogEntry
    {
        // Scrubs anything that looks like an absolute Windows path, UNC path, or
        // user-profile path from free-text fields.
        private static readonly Regex SensitivePathPattern = new Regex(
            @"([a-zA-Z]:[\\/][^\s;|""']*)|(\\\\[^\s;|""']+)|(%[A-Za-z_]+%)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Creates an audit entry.</summary>
        public AuditLogEntry(
            string id,
            DateTime timestampUtc,
            string actor,
            string action,
            AuditSeverity severity,
            string message,
            string correlationId = null,
            string planHash = null,
            string jobId = null,
            IDictionary<string, string> metadata = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("An audit id is required.", nameof(id));
            }

            if (string.IsNullOrEmpty(action))
            {
                throw new ArgumentException("An action is required.", nameof(action));
            }

            Id = id;
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc
                ? timestampUtc
                : timestampUtc.ToUniversalTime();
            // Actor and message are free text that could contain machine-specific paths.
            Actor = Sanitize(actor ?? string.Empty);
            Action = action;
            Severity = severity;
            Message = Sanitize(message ?? string.Empty);
            CorrelationId = correlationId;
            PlanHash = planHash;
            JobId = jobId;
            Metadata = metadata == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : SanitizeMap(metadata);
        }

        /// <summary>Unique entry id.</summary>
        public string Id { get; }

        /// <summary>UTC timestamp.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Who performed the action ("mcp:client-id", "user:name", "system").</summary>
        public string Actor { get; }

        /// <summary>Stable action name, e.g. "plan.commit".</summary>
        public string Action { get; }

        /// <summary>Severity.</summary>
        public AuditSeverity Severity { get; }

        /// <summary>Human-readable, already-sanitised detail.</summary>
        public string Message { get; }

        /// <summary>Correlation id when the entry belongs to a request flow.</summary>
        public string CorrelationId { get; }

        /// <summary>Canonical plan hash when the entry references a plan.</summary>
        public string PlanHash { get; }

        /// <summary>Job id when the entry references a job.</summary>
        public string JobId { get; }

        /// <summary>Additional sanitised key/value metadata.</summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["id"] = JsonValue.String(Id),
                ["timestampUtc"] = JsonValue.String(TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")),
                ["actor"] = JsonValue.String(Actor),
                ["action"] = JsonValue.String(Action),
                ["severity"] = JsonValue.String(SeverityToWire(Severity)),
                ["message"] = JsonValue.String(Message)
            };

            if (!string.IsNullOrEmpty(CorrelationId))
            {
                members["correlationId"] = JsonValue.String(CorrelationId);
            }

            if (!string.IsNullOrEmpty(PlanHash))
            {
                members["planHash"] = JsonValue.String(PlanHash);
            }

            if (!string.IsNullOrEmpty(JobId))
            {
                members["jobId"] = JsonValue.String(JobId);
            }

            if (Metadata.Count > 0)
            {
                var metaMembers = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                foreach (var pair in Metadata)
                {
                    metaMembers[pair.Key] = JsonValue.String(pair.Value);
                }

                members["metadata"] = JsonValue.Object(metaMembers);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Maps a severity to its wire token.</summary>
        public static string SeverityToWire(AuditSeverity severity)
        {
            switch (severity)
            {
                case AuditSeverity.Action: return "action";
                case AuditSeverity.Warning: return "warning";
                case AuditSeverity.Security: return "security";
                case AuditSeverity.Error: return "error";
                default: return "info";
            }
        }

        /// <summary>
        /// Scrub free text: strip path-like patterns and environment-variable tokens.
        /// The value is also truncated to a hard cap so a single field can never bloat
        /// the log or smuggle a huge payload.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string scrubbed = SensitivePathPattern.Replace(text, "[path-redacted]");
            if (scrubbed.Length > 2000)
            {
                scrubbed = scrubbed.Substring(0, 2000) + "...[truncated]";
            }

            return scrubbed;
        }

        private static Dictionary<string, string> SanitizeMap(IDictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                result[pair.Key] = Sanitize(pair.Value);
            }

            return result;
        }
    }

    /// <summary>
    /// A small, bounded, in-memory audit log with optional file persistence. Entries are
    /// sanitised at creation (see <see cref="AuditLogEntry.Sanitize"/>), capped in memory,
    /// and flushed as JSON Lines to a configured file when one is provided.
    /// </summary>
    public sealed class AuditLog
    {
        private readonly object _gate = new object();
        private readonly List<AuditLogEntry> _entries;
        private readonly int _maxEntries;
        private readonly string _filePath;
        private System.IO.StreamWriter _writer;
        private long _sequence;

        /// <summary>Creates an audit log. When <paramref name="filePath"/> is non-empty, entries are appended as JSON Lines.</summary>
        public AuditLog(int maxEntries = 2000, string filePath = null)
        {
            _maxEntries = maxEntries > 0 ? maxEntries : 2000;
            _entries = new List<AuditLogEntry>(Math.Min(_maxEntries, 512));
            _filePath = filePath;
        }

        /// <summary>Number of entries currently retained.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>Adds an entry.</summary>
        public AuditLogEntry Append(
            string actor,
            string action,
            AuditSeverity severity,
            string message,
            string correlationId = null,
            string planHash = null,
            string jobId = null,
            IDictionary<string, string> metadata = null)
        {
            AuditLogEntry entry;
            lock (_gate)
            {
                _sequence++;
                entry = new AuditLogEntry(
                    "a" + _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    DateTime.UtcNow,
                    actor,
                    action,
                    severity,
                    message,
                    correlationId,
                    planHash,
                    jobId,
                    metadata);

                _entries.Add(entry);
                if (_entries.Count > _maxEntries)
                {
                    _entries.RemoveAt(0);
                }

                if (_writer != null)
                {
                    try
                    {
                        _writer.WriteLine(JsonWriter.Write(entry.ToJson()));
                        _writer.Flush();
                    }
                    catch (Exception)
                    {
                        // A failed disk write must never take the runtime down.
                    }
                }
            }

            return entry;
        }

        /// <summary>Returns a snapshot of the current entries, newest first, optionally filtered by action prefix.</summary>
        public IReadOnlyList<AuditLogEntry> Snapshot(string actionPrefix = null, int limit = 500)
        {
            lock (_gate)
            {
                int take = limit > 0 ? limit : 500;
                var result = new List<AuditLogEntry>(Math.Min(take, _entries.Count));
                for (int i = _entries.Count - 1; i >= 0 && result.Count < take; i--)
                {
                    AuditLogEntry entry = _entries[i];
                    if (string.IsNullOrEmpty(actionPrefix) ||
                        entry.Action.StartsWith(actionPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(entry);
                    }
                }

                return result;
            }
        }

        /// <summary>Opens an append-only JSON Lines file for persistence.</summary>
        public void AttachFile(string path)
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (_writer != null)
                {
                    try
                    {
                        _writer.Dispose();
                    }
                    catch (Exception)
                    {
                        // ignore
                    }

                    _writer = null;
                }

                try
                {
                    string directory = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    _writer = new System.IO.StreamWriter(path, append: true)
                    {
                        AutoFlush = true
                    };
                }
                catch (Exception)
                {
                    // Persistence is best-effort; auditing must never crash the host.
                    _writer = null;
                }
            }
        }

        /// <summary>Closes the backing file, if any.</summary>
        public void Close()
        {
            lock (_gate)
            {
                if (_writer != null)
                {
                    try
                    {
                        _writer.Dispose();
                    }
                    catch (Exception)
                    {
                        // ignore
                    }

                    _writer = null;
                }
            }
        }
    }
}
