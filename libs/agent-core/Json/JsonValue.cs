using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutodeskNativeAgent.Core.Json
{
    /// <summary>The discriminator for <see cref="JsonValue"/>.</summary>
    public enum JsonKind
    {
        Null = 0,
        Boolean = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5
    }

    /// <summary>
    /// A minimal, allocation-conscious JSON DOM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Revit add-in deliberately avoids Newtonsoft.Json and System.Text.Json.
    /// Revit loads every add-in into a single AppDomain, so two add-ins bringing
    /// incompatible versions of the same serializer is a well-known and painful
    /// failure mode. Owning the parser also lets us enforce the protocol's
    /// hard limits (payload size, nesting depth) directly in the reader instead
    /// of after materialising an untrusted document.
    /// </para>
    /// <para>This type is immutable once constructed by the parser.</para>
    /// </remarks>
    public sealed class JsonValue
    {
        private readonly bool _boolean;
        private readonly double _number;
        private readonly string _string;
        private readonly List<JsonValue> _array;
        private readonly Dictionary<string, JsonValue> _object;

        /// <summary>The singleton JSON <c>null</c>.</summary>
        public static readonly JsonValue Null = new JsonValue();

        private JsonValue()
        {
            Kind = JsonKind.Null;
        }

        private JsonValue(bool value)
        {
            Kind = JsonKind.Boolean;
            _boolean = value;
        }

        private JsonValue(double value)
        {
            Kind = JsonKind.Number;
            _number = value;
        }

        private JsonValue(string value)
        {
            Kind = JsonKind.String;
            _string = value ?? throw new ArgumentNullException(nameof(value));
        }

        private JsonValue(List<JsonValue> items)
        {
            Kind = JsonKind.Array;
            _array = items ?? throw new ArgumentNullException(nameof(items));
        }

        private JsonValue(Dictionary<string, JsonValue> members)
        {
            Kind = JsonKind.Object;
            _object = members ?? throw new ArgumentNullException(nameof(members));
        }

        /// <summary>Gets the kind of this value.</summary>
        public JsonKind Kind { get; }

        /// <summary>Creates a boolean value.</summary>
        public static JsonValue Bool(bool value) => new JsonValue(value);

        /// <summary>Creates a numeric value.</summary>
        public static JsonValue Number(double value) => new JsonValue(value);

        /// <summary>Creates a string value.</summary>
        public static JsonValue String(string value) => new JsonValue(value);

        /// <summary>Creates an array value that takes ownership of <paramref name="items"/>.</summary>
        public static JsonValue Array(List<JsonValue> items) => new JsonValue(items);

        /// <summary>Creates an empty array value.</summary>
        public static JsonValue EmptyArray() => new JsonValue(new List<JsonValue>());

        /// <summary>Creates an object value that takes ownership of <paramref name="members"/>.</summary>
        public static JsonValue Object(Dictionary<string, JsonValue> members) => new JsonValue(members);

        /// <summary>Creates an empty object value.</summary>
        public static JsonValue EmptyObject() =>
            new JsonValue(new Dictionary<string, JsonValue>(StringComparer.Ordinal));

        /// <summary>True when this value is JSON <c>null</c>.</summary>
        public bool IsNull => Kind == JsonKind.Null;

        /// <summary>True when this value is a JSON boolean.</summary>
        public bool IsBoolean => Kind == JsonKind.Boolean;

        /// <summary>True when this value is a JSON number.</summary>
        public bool IsNumber => Kind == JsonKind.Number;

        /// <summary>True when this value is a JSON string.</summary>
        public bool IsString => Kind == JsonKind.String;

        /// <summary>True when this value is a JSON object.</summary>
        public bool IsObject => Kind == JsonKind.Object;

        /// <summary>True when this value is a JSON array.</summary>
        public bool IsArray => Kind == JsonKind.Array;

        /// <summary>Number of members for objects, items for arrays, and 0 otherwise.</summary>
        public int Count =>
            Kind == JsonKind.Array ? _array.Count :
            Kind == JsonKind.Object ? _object.Count : 0;

        /// <summary>Enumerates array items. Empty for non-arrays.</summary>
        public IReadOnlyList<JsonValue> Items =>
            _array ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();

        /// <summary>Enumerates object members. Empty for non-objects.</summary>
        public IEnumerable<KeyValuePair<string, JsonValue>> Members =>
            _object ?? (IEnumerable<KeyValuePair<string, JsonValue>>)System.Array.Empty<KeyValuePair<string, JsonValue>>();

        /// <summary>
        /// Gets the member with the given name, or <see cref="Null"/> when this is not an
        /// object or the member is absent. Never throws, so callers can chain lookups.
        /// </summary>
        public JsonValue this[string name]
        {
            get
            {
                if (_object != null && name != null && _object.TryGetValue(name, out var found))
                {
                    return found;
                }

                return Null;
            }
        }

        /// <summary>Gets the array item at <paramref name="index"/>, or <see cref="Null"/> when out of range.</summary>
        public JsonValue this[int index] =>
            _array != null && index >= 0 && index < _array.Count ? _array[index] : Null;

        /// <summary>True when this object contains the named member.</summary>
        public bool Has(string name) => _object != null && name != null && _object.ContainsKey(name);

        /// <summary>Returns the string value, or <paramref name="fallback"/> when this is not a string.</summary>
        public string AsString(string fallback = null) => Kind == JsonKind.String ? _string : fallback;

        /// <summary>Returns the numeric value, or <paramref name="fallback"/> when this is not a number.</summary>
        public double AsDouble(double fallback = 0d) => Kind == JsonKind.Number ? _number : fallback;

        /// <summary>
        /// Returns the value as a 32-bit integer, or <paramref name="fallback"/> when this is
        /// not a number or is not integral within the representable range.
        /// </summary>
        public int AsInt(int fallback = 0)
        {
            if (Kind != JsonKind.Number)
            {
                return fallback;
            }

            if (_number < int.MinValue || _number > int.MaxValue)
            {
                return fallback;
            }

            return (int)System.Math.Round(_number, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Returns the value as a 64-bit integer, or <paramref name="fallback"/> when this is
        /// not a number. Revit 2024 exposes ElementId values as <see cref="long"/>.
        /// </summary>
        public long AsLong(long fallback = 0L)
        {
            if (Kind != JsonKind.Number)
            {
                return fallback;
            }

            if (_number < long.MinValue || _number > long.MaxValue)
            {
                return fallback;
            }

            return (long)System.Math.Round(_number, MidpointRounding.AwayFromZero);
        }

        /// <summary>Returns the boolean value, or <paramref name="fallback"/> when this is not a boolean.</summary>
        public bool AsBool(bool fallback = false) => Kind == JsonKind.Boolean ? _boolean : fallback;

        /// <summary>
        /// True when this is a number; outputs the value. Used where a missing number and a
        /// present zero must be distinguished (offsets, elevations).
        /// </summary>
        public bool TryGetDouble(out double value)
        {
            if (Kind == JsonKind.Number)
            {
                value = _number;
                return true;
            }

            value = 0d;
            return false;
        }

        /// <summary>True when this is a non-empty string; outputs the value.</summary>
        public bool TryGetNonEmptyString(out string value)
        {
            if (Kind == JsonKind.String && !string.IsNullOrEmpty(_string))
            {
                value = _string;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>Serialises this value to compact JSON.</summary>
        public override string ToString() => JsonWriter.Write(this);

        /// <summary>Formats a double using invariant, round-trippable JSON number syntax.</summary>
        internal static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // JSON has no representation for these; the writer rejects them upstream.
                throw new JsonException("NaN and Infinity cannot be serialised to JSON.");
            }

            // "R" round-trips; integral values are emitted without a trailing ".0".
            if (value == System.Math.Floor(value) && System.Math.Abs(value) < 1e15)
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
