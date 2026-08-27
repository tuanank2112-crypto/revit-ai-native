using System;
using System.Collections.Generic;
using System.Text;

namespace AutodeskNativeAgent.Core.Json
{
    /// <summary>
    /// Serialises a <see cref="JsonValue"/> DOM to text.
    /// </summary>
    /// <remarks>
    /// Two modes are supported. <see cref="Write"/> emits compact JSON for transport.
    /// <see cref="WriteCanonical"/> emits a canonical form with object members ordered
    /// by ordinal key comparison, which is what <c>PlanHasher</c> hashes: a plan must
    /// produce the same hash regardless of the member order the agent happened to send.
    /// </remarks>
    public static class JsonWriter
    {
        /// <summary>Serialises to compact JSON.</summary>
        public static string Write(JsonValue value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, canonical: false, indent: -1, depth: 0);
            return sb.ToString();
        }

        /// <summary>
        /// Serialises to canonical JSON: compact, with object members sorted by
        /// ordinal key order so the output is stable across sources.
        /// </summary>
        public static string WriteCanonical(JsonValue value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, canonical: true, indent: -1, depth: 0);
            return sb.ToString();
        }

        /// <summary>Serialises to indented JSON, for logs and human-facing reports.</summary>
        public static string WriteIndented(JsonValue value, int indentSize = 2)
        {
            if (indentSize < 0)
            {
                indentSize = 0;
            }

            var sb = new StringBuilder(512);
            WriteValue(sb, value, canonical: false, indent: indentSize, depth: 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, JsonValue value, bool canonical, int indent, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    return;

                case JsonKind.Boolean:
                    sb.Append(value.AsBool() ? "true" : "false");
                    return;

                case JsonKind.Number:
                    sb.Append(JsonValue.FormatNumber(value.AsDouble()));
                    return;

                case JsonKind.String:
                    WriteString(sb, value.AsString(string.Empty));
                    return;

                case JsonKind.Array:
                    WriteArray(sb, value, canonical, indent, depth);
                    return;

                case JsonKind.Object:
                    WriteObject(sb, value, canonical, indent, depth);
                    return;

                default:
                    throw new JsonException("Unknown JSON kind '" + value.Kind + "'.");
            }
        }

        private static void WriteArray(StringBuilder sb, JsonValue value, bool canonical, int indent, int depth)
        {
            var items = value.Items;

            if (items.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append('[');

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                NewLineAndIndent(sb, indent, depth + 1);
                WriteValue(sb, items[i], canonical, indent, depth + 1);
            }

            NewLineAndIndent(sb, indent, depth);
            sb.Append(']');
        }

        private static void WriteObject(StringBuilder sb, JsonValue value, bool canonical, int indent, int depth)
        {
            if (value.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            // Materialise members so we can sort for the canonical form. Dictionary
            // enumeration order is an implementation detail and must never leak into a hash.
            var names = new List<string>(value.Count);
            foreach (var member in value.Members)
            {
                names.Add(member.Key);
            }

            if (canonical)
            {
                names.Sort(StringComparer.Ordinal);
            }

            sb.Append('{');

            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                NewLineAndIndent(sb, indent, depth + 1);
                WriteString(sb, names[i]);
                sb.Append(':');

                if (indent >= 0)
                {
                    sb.Append(' ');
                }

                WriteValue(sb, value[names[i]], canonical, indent, depth + 1);
            }

            NewLineAndIndent(sb, indent, depth);
            sb.Append('}');
        }

        private static void NewLineAndIndent(StringBuilder sb, int indent, int depth)
        {
            if (indent < 0)
            {
                return;
            }

            sb.Append('\n');
            sb.Append(' ', indent * depth);
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
