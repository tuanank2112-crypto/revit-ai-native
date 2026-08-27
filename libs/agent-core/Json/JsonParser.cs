using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutodeskNativeAgent.Core.Json
{
    /// <summary>Raised when JSON input is malformed or violates a configured limit.</summary>
    public sealed class JsonException : Exception
    {
        /// <summary>Creates a new instance with the supplied message.</summary>
        public JsonException(string message) : base(message)
        {
        }

        /// <summary>Creates a new instance with the supplied message and position.</summary>
        public JsonException(string message, int position)
            : base(message + " (at offset " + position.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Position = position;
        }

        /// <summary>Zero-based character offset where parsing failed, when known.</summary>
        public int Position { get; }
    }

    /// <summary>Limits applied while parsing untrusted JSON.</summary>
    public sealed class JsonLimits
    {
        /// <summary>Maximum accepted input length in characters. Default 8 MiB.</summary>
        public int MaxLength { get; set; } = 8 * 1024 * 1024;

        /// <summary>Maximum object/array nesting depth. Default 64.</summary>
        public int MaxDepth { get; set; } = 64;

        /// <summary>Maximum number of members in a single object. Default 4096.</summary>
        public int MaxObjectMembers { get; set; } = 4096;

        /// <summary>Maximum number of items in a single array. Default 100000.</summary>
        public int MaxArrayItems { get; set; } = 100000;

        /// <summary>The default limits used when none are supplied.</summary>
        public static JsonLimits Default => new JsonLimits();
    }

    /// <summary>
    /// A strict, recursive-descent JSON parser producing a <see cref="JsonValue"/> DOM.
    /// </summary>
    /// <remarks>
    /// Strictness is intentional: this parser sits on the boundary where untrusted
    /// agent input enters the Revit process. It rejects trailing commas, comments,
    /// NaN/Infinity literals, unquoted keys, and control characters in strings, and
    /// enforces depth and size limits while reading rather than afterwards.
    /// </remarks>
    public static class JsonParser
    {
        /// <summary>Parses <paramref name="text"/> using <see cref="JsonLimits.Default"/>.</summary>
        public static JsonValue Parse(string text) => Parse(text, JsonLimits.Default);

        /// <summary>Parses <paramref name="text"/> under the supplied limits.</summary>
        /// <exception cref="JsonException">The input is malformed or exceeds a limit.</exception>
        public static JsonValue Parse(string text, JsonLimits limits)
        {
            if (text == null)
            {
                throw new JsonException("JSON input was null.");
            }

            if (limits == null)
            {
                limits = JsonLimits.Default;
            }

            if (text.Length > limits.MaxLength)
            {
                throw new JsonException(
                    "JSON input of " + text.Length.ToString(CultureInfo.InvariantCulture) +
                    " characters exceeds the maximum of " +
                    limits.MaxLength.ToString(CultureInfo.InvariantCulture) + ".");
            }

            var state = new ParserState(text, limits);
            state.SkipWhitespace();
            JsonValue value = ParseValue(state, 0);
            state.SkipWhitespace();

            if (!state.AtEnd)
            {
                throw new JsonException("Unexpected trailing content after the JSON value.", state.Position);
            }

            return value;
        }

        /// <summary>Attempts to parse; returns false instead of throwing.</summary>
        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonException ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        private static JsonValue ParseValue(ParserState state, int depth)
        {
            if (depth > state.Limits.MaxDepth)
            {
                throw new JsonException(
                    "JSON nesting depth exceeds the maximum of " +
                    state.Limits.MaxDepth.ToString(CultureInfo.InvariantCulture) + ".",
                    state.Position);
            }

            if (state.AtEnd)
            {
                throw new JsonException("Unexpected end of JSON input.", state.Position);
            }

            char c = state.Current;
            switch (c)
            {
                case '{':
                    return ParseObject(state, depth);
                case '[':
                    return ParseArray(state, depth);
                case '"':
                    return JsonValue.String(ParseString(state));
                case 't':
                    state.Expect("true");
                    return JsonValue.Bool(true);
                case 'f':
                    state.Expect("false");
                    return JsonValue.Bool(false);
                case 'n':
                    state.Expect("null");
                    return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return JsonValue.Number(ParseNumber(state));
                    }

                    throw new JsonException("Unexpected character '" + c + "' in JSON.", state.Position);
            }
        }

        private static JsonValue ParseObject(ParserState state, int depth)
        {
            state.Advance(); // consume '{'
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            state.SkipWhitespace();
            if (state.AtEnd)
            {
                throw new JsonException("Unterminated JSON object.", state.Position);
            }

            if (state.Current == '}')
            {
                state.Advance();
                return JsonValue.Object(members);
            }

            while (true)
            {
                state.SkipWhitespace();

                if (state.AtEnd)
                {
                    throw new JsonException("Unterminated JSON object.", state.Position);
                }

                if (state.Current != '"')
                {
                    throw new JsonException("Object member names must be quoted strings.", state.Position);
                }

                string name = ParseString(state);

                state.SkipWhitespace();
                if (state.AtEnd || state.Current != ':')
                {
                    throw new JsonException("Expected ':' after the object member name.", state.Position);
                }

                state.Advance(); // consume ':'
                state.SkipWhitespace();

                JsonValue value = ParseValue(state, depth + 1);

                if (members.Count >= state.Limits.MaxObjectMembers)
                {
                    throw new JsonException(
                        "JSON object exceeds the maximum of " +
                        state.Limits.MaxObjectMembers.ToString(CultureInfo.InvariantCulture) + " members.",
                        state.Position);
                }

                // Last value wins on duplicate keys, matching mainstream parsers.
                members[name] = value;

                state.SkipWhitespace();
                if (state.AtEnd)
                {
                    throw new JsonException("Unterminated JSON object.", state.Position);
                }

                if (state.Current == ',')
                {
                    state.Advance();
                    state.SkipWhitespace();

                    // Reject trailing commas explicitly rather than silently accepting them.
                    if (!state.AtEnd && state.Current == '}')
                    {
                        throw new JsonException("Trailing comma in JSON object.", state.Position);
                    }

                    continue;
                }

                if (state.Current == '}')
                {
                    state.Advance();
                    return JsonValue.Object(members);
                }

                throw new JsonException("Expected ',' or '}' in JSON object.", state.Position);
            }
        }

        private static JsonValue ParseArray(ParserState state, int depth)
        {
            state.Advance(); // consume '['
            var items = new List<JsonValue>();

            state.SkipWhitespace();
            if (state.AtEnd)
            {
                throw new JsonException("Unterminated JSON array.", state.Position);
            }

            if (state.Current == ']')
            {
                state.Advance();
                return JsonValue.Array(items);
            }

            while (true)
            {
                state.SkipWhitespace();
                JsonValue value = ParseValue(state, depth + 1);

                if (items.Count >= state.Limits.MaxArrayItems)
                {
                    throw new JsonException(
                        "JSON array exceeds the maximum of " +
                        state.Limits.MaxArrayItems.ToString(CultureInfo.InvariantCulture) + " items.",
                        state.Position);
                }

                items.Add(value);

                state.SkipWhitespace();
                if (state.AtEnd)
                {
                    throw new JsonException("Unterminated JSON array.", state.Position);
                }

                if (state.Current == ',')
                {
                    state.Advance();
                    state.SkipWhitespace();

                    if (!state.AtEnd && state.Current == ']')
                    {
                        throw new JsonException("Trailing comma in JSON array.", state.Position);
                    }

                    continue;
                }

                if (state.Current == ']')
                {
                    state.Advance();
                    return JsonValue.Array(items);
                }

                throw new JsonException("Expected ',' or ']' in JSON array.", state.Position);
            }
        }

        private static string ParseString(ParserState state)
        {
            state.Advance(); // consume opening quote
            var sb = new StringBuilder();

            while (true)
            {
                if (state.AtEnd)
                {
                    throw new JsonException("Unterminated JSON string.", state.Position);
                }

                char c = state.Current;

                if (c == '"')
                {
                    state.Advance();
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    state.Advance();
                    if (state.AtEnd)
                    {
                        throw new JsonException("Unterminated escape sequence in JSON string.", state.Position);
                    }

                    char esc = state.Current;
                    state.Advance();

                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append(ParseUnicodeEscape(state));
                            break;
                        default:
                            throw new JsonException("Invalid escape '\\" + esc + "' in JSON string.", state.Position);
                    }

                    continue;
                }

                if (c < 0x20)
                {
                    throw new JsonException(
                        "Unescaped control character U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) +
                        " in JSON string.",
                        state.Position);
                }

                sb.Append(c);
                state.Advance();
            }
        }

        private static char ParseUnicodeEscape(ParserState state)
        {
            if (state.Position + 4 > state.Length)
            {
                throw new JsonException("Truncated \\u escape in JSON string.", state.Position);
            }

            int code = 0;
            for (int i = 0; i < 4; i++)
            {
                char h = state.Current;
                int digit;

                if (h >= '0' && h <= '9') digit = h - '0';
                else if (h >= 'a' && h <= 'f') digit = h - 'a' + 10;
                else if (h >= 'A' && h <= 'F') digit = h - 'A' + 10;
                else throw new JsonException("Invalid hex digit '" + h + "' in \\u escape.", state.Position);

                code = (code << 4) + digit;
                state.Advance();
            }

            // Surrogate pairs are preserved as-is; the StringBuilder concatenates them
            // into a valid UTF-16 pair when both halves are present in the input.
            return (char)code;
        }

        private static double ParseNumber(ParserState state)
        {
            int start = state.Position;

            if (!state.AtEnd && state.Current == '-')
            {
                state.Advance();
            }

            // Integer part: either a lone '0' or [1-9][0-9]*. Leading zeros are invalid JSON.
            if (state.AtEnd)
            {
                throw new JsonException("Truncated JSON number.", state.Position);
            }

            if (state.Current == '0')
            {
                state.Advance();
            }
            else if (state.Current >= '1' && state.Current <= '9')
            {
                while (!state.AtEnd && state.Current >= '0' && state.Current <= '9')
                {
                    state.Advance();
                }
            }
            else
            {
                throw new JsonException("Invalid JSON number.", state.Position);
            }

            // Fractional part.
            if (!state.AtEnd && state.Current == '.')
            {
                state.Advance();

                if (state.AtEnd || state.Current < '0' || state.Current > '9')
                {
                    throw new JsonException("JSON number has no digits after the decimal point.", state.Position);
                }

                while (!state.AtEnd && state.Current >= '0' && state.Current <= '9')
                {
                    state.Advance();
                }
            }

            // Exponent.
            if (!state.AtEnd && (state.Current == 'e' || state.Current == 'E'))
            {
                state.Advance();

                if (!state.AtEnd && (state.Current == '+' || state.Current == '-'))
                {
                    state.Advance();
                }

                if (state.AtEnd || state.Current < '0' || state.Current > '9')
                {
                    throw new JsonException("JSON number has no digits in the exponent.", state.Position);
                }

                while (!state.AtEnd && state.Current >= '0' && state.Current <= '9')
                {
                    state.Advance();
                }
            }

            string slice = state.Slice(start, state.Position - start);

            double result;
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                throw new JsonException("JSON number '" + slice + "' could not be represented as a double.", start);
            }

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                throw new JsonException("JSON number '" + slice + "' overflows a double.", start);
            }

            return result;
        }

        private sealed class ParserState
        {
            private readonly string _text;

            internal ParserState(string text, JsonLimits limits)
            {
                _text = text;
                Limits = limits;
                Position = 0;
            }

            internal JsonLimits Limits { get; }

            internal int Position { get; private set; }

            internal int Length => _text.Length;

            internal bool AtEnd => Position >= _text.Length;

            internal char Current => _text[Position];

            internal void Advance() => Position++;

            internal string Slice(int start, int length) => _text.Substring(start, length);

            internal void SkipWhitespace()
            {
                while (Position < _text.Length)
                {
                    char c = _text[Position];

                    // Only the four JSON-defined whitespace characters. Comments are not JSON.
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        Position++;
                        continue;
                    }

                    break;
                }
            }

            internal void Expect(string literal)
            {
                if (Position + literal.Length > _text.Length ||
                    string.CompareOrdinal(_text, Position, literal, 0, literal.Length) != 0)
                {
                    throw new JsonException("Expected the literal '" + literal + "'.", Position);
                }

                Position += literal.Length;
            }
        }
    }
}
