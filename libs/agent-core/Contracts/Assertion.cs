using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>The supported post-execution checks, mirroring assertion.schema.json.</summary>
    public enum AssertionKind
    {
        /// <summary>Element exists (or not) for a reference target.</summary>
        ElementExists = 0,

        /// <summary>Number of elements matching the target query.</summary>
        ElementCount = 1,

        /// <summary>Category of the target element.</summary>
        Category = 2,

        /// <summary>Type name of the target element.</summary>
        TypeName = 3,

        /// <summary>Name of the level hosting the target element.</summary>
        LevelName = 4,

        /// <summary>Length of the target element.</summary>
        Length = 5,

        /// <summary>Height of the target element.</summary>
        Height = 6,

        /// <summary>Width of the target element.</summary>
        Width = 7,

        /// <summary>Position of the target element.</summary>
        Position = 8,

        /// <summary>Angle of the target element.</summary>
        Angle = 9,

        /// <summary>Equality of a named parameter.</summary>
        ParameterEquals = 10,

        /// <summary>Non-emptiness of a named parameter.</summary>
        ParameterNotEmpty = 11,

        /// <summary>Expected host of the target element.</summary>
        HostEquals = 12,

        /// <summary>Target element bounding box contained in a region.</summary>
        BoundingBoxWithin = 13
    }

    /// <summary>
    /// A post-execution check attached to a plan operation. Assertions are evaluated by
    /// the runtime after each operation; a failed mandatory assertion rolls the plan back.
    /// </summary>
    public sealed class Assertion
    {
        /// <summary>The default tolerance for numeric comparisons, in plan units or degrees.</summary>
        public const double DefaultTolerance = 0.5;

        /// <summary>Creates an assertion.</summary>
        public Assertion(
            AssertionKind kind,
            string target,
            JsonValue equals = null,
            string unit = null,
            double tolerance = DefaultTolerance,
            JsonValue expect = null,
            string parameter = null,
            string host = null,
            JsonValue region = null)
        {
            Kind = kind;
            Target = target ?? string.Empty;
            Equals = equals ?? JsonValue.Null;
            Unit = unit;
            Tolerance = tolerance;
            Expect = expect ?? JsonValue.Null;
            Parameter = parameter;
            Host = host;
            Region = region;
        }

        /// <summary>The kind of check.</summary>
        public AssertionKind Kind { get; }

        /// <summary>Element reference, e.g. <c>$result.op1</c>.</summary>
        public string Target { get; }

        /// <summary>Expected scalar value for equality-style kinds.</summary>
        public new JsonValue Equals { get; }

        /// <summary>Unit the <c>Equals</c> value is expressed in, when it is a length or angle.</summary>
        public string Unit { get; }

        /// <summary>Allowed deviation for numeric checks.</summary>
        public double Tolerance { get; }

        /// <summary>Expected bool (element_exists) or count (element_count).</summary>
        public JsonValue Expect { get; }

        /// <summary>Parameter name/GUID/BuiltIn name for parameter kinds.</summary>
        public string Parameter { get; }

        /// <summary>Expected host reference for <see cref="AssertionKind.HostEquals"/>.</summary>
        public string Host { get; }

        /// <summary>Region for <see cref="AssertionKind.BoundingBoxWithin"/>.</summary>
        public JsonValue Region { get; }

        /// <summary>Parses an assertion from the wire form. Returns null for non-objects.</summary>
        public static Assertion FromJson(JsonValue json)
        {
            if (json == null || !json.IsObject)
            {
                return null;
            }

            AssertionKind kind;
            string kindText = json["kind"].AsString(null);
            if (!TryParseKind(kindText, out kind))
            {
                return null;
            }

            string target = json["target"].AsString(null);
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            double tolerance = json["tolerance"].AsDouble(DefaultTolerance);
            if (tolerance < 0)
            {
                tolerance = DefaultTolerance;
            }

            return new Assertion(
                kind,
                target,
                json["equals"],
                json["unit"].AsString(null),
                tolerance,
                json["expect"],
                json["parameter"].AsString(null),
                json["host"].AsString(null),
                json["region"]);
        }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["kind"] = JsonValue.String(ToWire(Kind)),
                ["target"] = JsonValue.String(Target)
            };

            if (!Equals.IsNull)
            {
                members["equals"] = Equals;
            }

            if (!string.IsNullOrEmpty(Unit))
            {
                members["unit"] = JsonValue.String(Unit);
            }

            members["tolerance"] = JsonValue.Number(Tolerance);

            if (!Expect.IsNull)
            {
                members["expect"] = Expect;
            }

            if (!string.IsNullOrEmpty(Parameter))
            {
                members["parameter"] = JsonValue.String(Parameter);
            }

            if (!string.IsNullOrEmpty(Host))
            {
                members["host"] = JsonValue.String(Host);
            }

            if (!Region.IsNull)
            {
                members["region"] = Region;
            }

            return JsonValue.Object(members);
        }

        /// <summary>Maps a wire token to an <see cref="AssertionKind"/>.</summary>
        public static bool TryParseKind(string text, out AssertionKind kind)
        {
            switch (text)
            {
                case "element_exists": kind = AssertionKind.ElementExists; break;
                case "element_count": kind = AssertionKind.ElementCount; break;
                case "category": kind = AssertionKind.Category; break;
                case "type_name": kind = AssertionKind.TypeName; break;
                case "level_name": kind = AssertionKind.LevelName; break;
                case "length": kind = AssertionKind.Length; break;
                case "height": kind = AssertionKind.Height; break;
                case "width": kind = AssertionKind.Width; break;
                case "position": kind = AssertionKind.Position; break;
                case "angle": kind = AssertionKind.Angle; break;
                case "parameter_equals": kind = AssertionKind.ParameterEquals; break;
                case "parameter_not_empty": kind = AssertionKind.ParameterNotEmpty; break;
                case "host_equals": kind = AssertionKind.HostEquals; break;
                case "bounding_box_within": kind = AssertionKind.BoundingBoxWithin; break;
                default:
                    kind = AssertionKind.ElementExists;
                    return false;
            }

            return true;
        }

        /// <summary>Maps an <see cref="AssertionKind"/> back to its wire token.</summary>
        public static string ToWire(AssertionKind kind)
        {
            switch (kind)
            {
                case AssertionKind.ElementCount: return "element_count";
                case AssertionKind.Category: return "category";
                case AssertionKind.TypeName: return "type_name";
                case AssertionKind.LevelName: return "level_name";
                case AssertionKind.Length: return "length";
                case AssertionKind.Height: return "height";
                case AssertionKind.Width: return "width";
                case AssertionKind.Position: return "position";
                case AssertionKind.Angle: return "angle";
                case AssertionKind.ParameterEquals: return "parameter_equals";
                case AssertionKind.ParameterNotEmpty: return "parameter_not_empty";
                case AssertionKind.HostEquals: return "host_equals";
                case AssertionKind.BoundingBoxWithin: return "bounding_box_within";
                default: return "element_exists";
            }
        }
    }
}
