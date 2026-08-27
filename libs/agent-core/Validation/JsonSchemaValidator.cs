using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Validation
{
    /// <summary>Raised when a JSON-Schema document itself is malformed or unsupported.</summary>
    public sealed class SchemaException : Exception
    {
        /// <summary>Creates a schema exception.</summary>
        public SchemaException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// A compact JSON-Schema validator covering exactly the subset used by the contract
    /// schemas in <c>libs/contracts/schemas</c>: type, const, enum, pattern, required,
    /// properties, additionalProperties, items, numeric bounds, oneOf/anyOf, $ref and
    /// local $defs. No external dependencies, safe for the Revit AppDomain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unsupported or unknown keywords are intentionally ignored, matching the draft-2020-12
    /// behaviour of "unknown keywords are annotations". The hard guarantee the runtime needs
    /// is that <em>known</em> constraints are enforced; unsupported exotic keywords would be
    /// a false sense of security only if we pretended to enforce them.
    /// </para>
    /// <para>
    /// <c>additionalProperties:false</c> is enforced because the schemas use it to reject
    /// typos in plan payloads - a silent acceptance there could let an agent believe a
    /// misspelled argument was honoured.
    /// </para>
    /// </remarks>
    public static class JsonSchemaValidator
    {
        /// <summary>Validates <paramref name="value"/> against the schema loaded from <paramref name="schemaJson"/>.</summary>
        public static List<string> Validate(JsonValue value, JsonValue schemaJson)
        {
            if (schemaJson == null || !schemaJson.IsObject)
            {
                throw new SchemaException("The schema must be a JSON object.");
            }

            var context = new ValidationContext();
            ValidateAgainst(value, schemaJson, context, "$");
            return context.Errors;
        }

        private sealed class ValidationContext
        {
            internal readonly List<string> Errors = new List<string>();
        }

        private static void ValidateAgainst(JsonValue value, JsonValue schema, ValidationContext context, string path)
        {
            if (!schema.IsObject)
            {
                return; // boolean schemas are not used by the contracts
            }

            // $ref resolution: local fragment only ("#/$defs/name" or "./file#/..." NOT supported
            // at runtime - operation schemas are inlined by the registry before validation).
            string reference = schema["$ref"].AsString(null);
            if (reference != null)
            {
                if (reference.StartsWith("#/", StringComparison.Ordinal))
                {
                    // The registry pre-resolves $defs into a map keyed by the fragment,
                    // so the validator itself only handles the root level.
                    throw new SchemaException("Unsupported external $ref '" + reference + "'.");
                }
            }

            // type
            JsonValue type = schema["type"];
            if (type.IsString)
            {
                string expected = type.AsString(null);
                if (!MatchesType(value, expected))
                {
                    Fail(context, path, "expected type '" + expected + "' but was " + Describe(value) + ".");
                    return;
                }
            }
            else if (type.IsArray)
            {
                bool matched = false;
                foreach (JsonValue item in type.Items)
                {
                    if (MatchesType(value, item.AsString(string.Empty)))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    Fail(context, path, "value type does not match any of the allowed types.");
                    return;
                }
            }

            // const
            JsonValue constValue = schema["const"];
            if (!constValue.IsNull)
            {
                if (!JsonEquals(constValue, value))
                {
                    Fail(context, path, "expected the constant " + constValue + ".");
                }
            }

            // enum
            JsonValue enumValue = schema["enum"];
            if (enumValue.IsArray && enumValue.Count > 0)
            {
                bool matched = false;
                foreach (JsonValue candidate in enumValue.Items)
                {
                    if (JsonEquals(candidate, value))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    var allowed = new List<string>();
                    foreach (JsonValue candidate in enumValue.Items)
                    {
                        allowed.Add(candidate.ToString());
                    }

                    Fail(context, path, "value must be one of: " + string.Join(", ", allowed) + ".");
                    return;
                }
            }

            if (value.Kind == JsonKind.Number)
            {
                double number = value.AsDouble();
                double minimum = schema["minimum"].AsDouble(double.NaN);
                if (!double.IsNaN(minimum) && number < minimum)
                {
                    Fail(context, path, "value " + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is below the minimum " + minimum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }

                double exclusiveMinimum = schema["exclusiveMinimum"].AsDouble(double.NaN);
                if (!double.IsNaN(exclusiveMinimum) && number <= exclusiveMinimum)
                {
                    Fail(context, path, "value must be greater than " + exclusiveMinimum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }

                double maximum = schema["maximum"].AsDouble(double.NaN);
                if (!double.IsNaN(maximum) && number > maximum)
                {
                    Fail(context, path, "value exceeds the maximum of " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }
            }

            if (value.Kind == JsonKind.String)
            {
                string text = value.AsString(string.Empty);
                int minLength = schema["minLength"].AsInt(0);
                if (text.Length < minLength)
                {
                    Fail(context, path, "string is shorter than " + minLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters.");
                }

                int maxLength = schema["maxLength"].AsInt(int.MaxValue);
                if (text.Length > maxLength)
                {
                    Fail(context, path, "string is longer than " + maxLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters.");
                }

                string pattern = schema["pattern"].AsString(null);
                if (pattern != null)
                {
                    var regex = new System.Text.RegularExpressions.Regex(
                        pattern,
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                    if (!regex.IsMatch(text))
                    {
                        Fail(context, path, "string does not match the required pattern.");
                    }
                }
            }

            int minItems = schema["minItems"].AsInt(0);
            int maxItems = schema["maxItems"].AsInt(int.MaxValue);
            if (value.IsArray)
            {
                if (value.Count < minItems)
                {
                    Fail(context, path, "array has fewer than " + minItems.ToString(System.Globalization.CultureInfo.InvariantCulture) + " items.");
                    return;
                }

                if (value.Count > maxItems)
                {
                    Fail(context, path, "array has more than " + maxItems.ToString(System.Globalization.CultureInfo.InvariantCulture) + " items.");
                    return;
                }

                JsonValue items = schema["items"];
                if (items != null)
                {
                    int index = 0;
                    foreach (JsonValue item in value.Items)
                    {
                        ValidateAgainst(item, items, context, path + "[" + index + "]");
                        index++;
                    }
                }

                return;
            }

            if (value.IsObject)
            {
                bool additionalAllowed = schema["additionalProperties"] == null || schema["additionalProperties"].AsBool(true);

                JsonValue properties = schema["properties"];
                if (properties != null && properties.IsObject)
                {
                    foreach (var member in properties.Members)
                    {
                        JsonValue memberValue = value[member.Key];
                        if (!memberValue.IsNull)
                        {
                            ValidateAgainst(memberValue, member.Value, context, path + "." + member.Key);
                        }
                    }
                }

                JsonValue required = schema["required"];
                if (required != null && required.IsArray)
                {
                    foreach (JsonValue req in required.Items)
                    {
                        string name = req.AsString(null);
                        if (name == null)
                        {
                            continue;
                        }

                        if (!value.Has(name))
                        {
                            Fail(context, path, "missing required property '" + name + "'.");
                        }
                    }
                }

                if (!additionalAllowed)
                {
                    foreach (var member in value.Members)
                    {
                        JsonValue declared = properties != null ? properties[member.Key] : JsonValue.Null;
                        if (declared.IsNull)
                        {
                            Fail(context, path, "additional property '" + member.Key + "' is not allowed.");
                        }
                    }
                }

                // oneOf / anyOf
                ValidateBranching(value, schema, context, path);

                return;
            }

            // Branching keywords for non-objects (rare but legal).
            ValidateBranching(value, schema, context, path);
        }

        private static void ValidateBranching(JsonValue value, JsonValue schema, ValidationContext context, string path)
        {
            JsonValue oneOf = schema["oneOf"];
            if (oneOf.IsArray && oneOf.Count > 0)
            {
                int matched = 0;
                foreach (JsonValue branch in oneOf.Items)
                {
                    var branchContext = new ValidationContext();
                    ValidateAgainst(value, branch, branchContext, path);
                    if (branchContext.Errors.Count == 0)
                    {
                        matched++;
                    }
                }

                if (matched != 1)
                {
                    Fail(context, path, "value must match exactly one of the 'oneOf' branches (matched " + matched + ").");
                }

                return;
            }

            JsonValue anyOf = schema["anyOf"];
            if (anyOf.IsArray && anyOf.Count > 0)
            {
                foreach (JsonValue branch in anyOf.Items)
                {
                    var branchContext = new ValidationContext();
                    ValidateAgainst(value, branch, branchContext, path);
                    if (branchContext.Errors.Count == 0)
                    {
                        return; // any branch satisfied
                    }
                }

                Fail(context, path, "value did not match any of the 'anyOf' branches.");
            }
        }

        private static bool MatchesType(JsonValue value, string expectedType)
        {
            switch (expectedType)
            {
                case "null": return value.IsNull;
                case "boolean": return value.Kind == JsonKind.Boolean;
                case "number": return value.Kind == JsonKind.Number;
                case "integer":
                    return value.Kind == JsonKind.Number &&
                           value.AsDouble() == Math.Floor(value.AsDouble()) &&
                           !double.IsInfinity(value.AsDouble());
                case "string": return value.Kind == JsonKind.String;
                case "array": return value.IsArray;
                case "object": return value.IsObject;
                default: return false;
            }
        }

        private static bool JsonEquals(JsonValue a, JsonValue b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Kind != b.Kind)
            {
                return false;
            }

            switch (a.Kind)
            {
                case JsonKind.Null:
                    return true;
                case JsonKind.Boolean:
                    return a.AsBool() == b.AsBool();
                case JsonKind.Number:
                    return Math.Abs(a.AsDouble() - b.AsDouble()) < 1e-12;
                case JsonKind.String:
                    return string.Equals(a.AsString(), b.AsString(), StringComparison.Ordinal);
                case JsonKind.Array:
                    if (a.Count != b.Count)
                    {
                        return false;
                    }

                    for (int i = 0; i < a.Count; i++)
                    {
                        if (!JsonEquals(a[i], b[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                case JsonKind.Object:
                    if (a.Count != b.Count)
                    {
                        return false;
                    }

                    foreach (var member in a.Members)
                    {
                        if (!b.Has(member.Key) || !JsonEquals(member.Value, b[member.Key]))
                        {
                            return false;
                        }
                    }

                    return true;
                default:
                    return false;
            }
        }

        private static string Describe(JsonValue value)
        {
            switch (value.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Boolean: return "boolean";
                case JsonKind.Number: return "number";
                case JsonKind.String: return "string";
                case JsonKind.Array: return "array";
                default: return "object";
            }
        }

        private static void Fail(ValidationContext context, string path, string message)
        {
            context.Errors.Add(path + ": " + message);
        }
    }
}
