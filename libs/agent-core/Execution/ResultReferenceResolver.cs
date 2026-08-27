using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Execution
{
    /// <summary>
    /// Resolves <c>$result.&lt;opId&gt;</c> references against the map of completed
    /// operation outcomes produced so far. Supports dotted paths into the result JSON
    /// (e.g. <c>$result.op1.createdElements[0].uniqueId</c>).
    /// </summary>
    public sealed class ResultReferenceResolver
    {
        private readonly IReadOnlyDictionary<string, JsonValue> _results;

        /// <summary>Creates a resolver over the given operation results.</summary>
        public ResultReferenceResolver(IReadOnlyDictionary<string, JsonValue> results)
        {
            _results = results ?? new Dictionary<string, JsonValue>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Tries to resolve a reference into a JSON value. A reference must start with
        /// <c>$result.</c>; anything else is returned unresolved (not an error) so callers
        /// can treat plain unique ids as themselves.
        /// </summary>
        public bool TryResolve(string reference, out JsonValue resolved, out string error)
        {
            resolved = null;
            error = null;

            if (string.IsNullOrEmpty(reference))
            {
                error = "Reference is empty.";
                return false;
            }

            if (!reference.StartsWith("$result.", StringComparison.Ordinal))
            {
                // Not a result reference; caller decides how to interpret.
                return false;
            }

            string path = reference.Substring("$result.".Length);
            string[] segments = path.Split('.');
            if (segments.Length == 0 || string.IsNullOrEmpty(segments[0]))
            {
                error = "Result reference '" + reference + "' has no operation id.";
                return false;
            }

            if (!_results.TryGetValue(segments[0], out JsonValue current))
            {
                error = "No result for operation '" + segments[0] + "'.";
                return false;
            }

            for (int i = 1; i < segments.Length; i++)
            {
                current = Navigate(current, segments[i], reference, out error);
                if (error != null)
                {
                    return false;
                }
            }

            resolved = current;
            return true;
        }

        /// <summary>
        /// Resolves a string that may be a reference, an explicit uniqueId, or a bare value.
        /// Used by element-reference consumers where the wire form is usually free-form.
        /// </summary>
        public static JsonValue ResolveOrSelf(string reference, IReadOnlyDictionary<string, JsonValue> results)
        {
            var resolver = new ResultReferenceResolver(results);
            JsonValue resolved;
            string error;
            if (resolver.TryResolve(reference, out resolved, out error))
            {
                return resolved;
            }

            return JsonValue.String(reference);
        }

        private static JsonValue Navigate(JsonValue current, string segment, string reference, out string error)
        {
            error = null;

            // Array indexing: segments like "elements[0]".
            int bracket = segment.IndexOf('[');
            if (bracket >= 0 && segment.EndsWith("]", StringComparison.Ordinal))
            {
                string arrayName = segment.Substring(0, bracket);
                string indexText = segment.Substring(bracket + 1, segment.Length - bracket - 2);
                int index;
                if (!int.TryParse(indexText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out index))
                {
                    error = "Invalid array index '" + indexText + "' in '" + reference + "'.";
                    return JsonValue.Null;
                }

                if (!string.IsNullOrEmpty(arrayName))
                {
                    current = current[arrayName];
                }

                if (current.IsArray && index >= 0 && index < current.Count)
                {
                    return current[index];
                }

                error = "Array index " + index + " out of range in '" + reference + "'.";
                return JsonValue.Null;
            }

            JsonValue next = current[segment];
            if (next.IsNull && !current.IsObject)
            {
                error = "Cannot navigate '" + segment + "' on a non-object in '" + reference + "'.";
                return JsonValue.Null;
            }

            return next;
        }
    }
}
