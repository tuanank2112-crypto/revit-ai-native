using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Validation;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>
    /// Loads the contract schemas from embedded resources and produces self-contained
    /// documents with every <c>$ref</c> inlined, so <see cref="JsonSchemaValidator"/> can
    /// enforce them without a schema-registry implementation. Cross-file references (e.g.
    /// door.insert's <c>wall.create.schema.json#/$defs/levelSelector</c> and
    /// element-reference.schema.json) are resolved at load time.
    /// </summary>
    public static class SchemaCatalog
    {
        private static readonly Dictionary<string, JsonValue> Cache = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        /// <summary>Loads and inlines the named operation schema.</summary>
        public static JsonValue LoadOperationSchema(string operationName)
        {
            // MSBuild's LogicalName keeps the subfolder separator as a literal
            // backslash (e.g. ...Schemas.operations\wall.create.schema.json).
            string resourceName = "AutodeskNativeAgent.Core.Schemas.operations\\" + operationName + ".schema.json";
            return LoadResolved(resourceName);
        }

        /// <summary>Loads a schema by resource name and inlines every $ref.</summary>
        public static JsonValue LoadResolved(string resourceName)
        {
            JsonValue cached;
            if (Cache.TryGetValue(resourceName, out cached))
            {
                return cached;
            }

            JsonValue schema = EmbeddedSchemas.Load(typeof(SchemaCatalog).Assembly, resourceName);
            schema = new RefResolver(schema).Resolve();
            Cache[resourceName] = schema;
            return schema;
        }

        /// <summary>Performs a deep copy of a JsonValue DOM.</summary>
        public static JsonValue DeepCopy(JsonValue value)
        {
            if (value == null)
            {
                return JsonValue.Null;
            }

            switch (value.Kind)
            {
                case JsonKind.Null:
                    return JsonValue.Null;
                case JsonKind.Boolean:
                    return JsonValue.Bool(value.AsBool());
                case JsonKind.Number:
                    return JsonValue.Number(value.AsDouble());
                case JsonKind.String:
                    return JsonValue.String(value.AsString(string.Empty));
                case JsonKind.Array:
                    var items = new List<JsonValue>(value.Count);
                    foreach (JsonValue item in value.Items)
                    {
                        items.Add(DeepCopy(item));
                    }

                    return JsonValue.Array(items);
                case JsonKind.Object:
                    var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                    foreach (var member in value.Members)
                    {
                        members[member.Key] = DeepCopy(member.Value);
                    }

                    return JsonValue.Object(members);
                default:
                    return JsonValue.Null;
            }
        }

        /// <summary>Inlines $refs inside one schema document, resolving cross-file references.</summary>
        private sealed class RefResolver
        {
            private readonly JsonValue _root;
            private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.Ordinal);

            internal RefResolver(JsonValue root)
            {
                _root = root;
            }

            internal JsonValue Resolve() => Inline(_root);

            private JsonValue Inline(JsonValue value)
            {
                if (value == null)
                {
                    return JsonValue.Null;
                }

                if (value.IsArray)
                {
                    var items = new List<JsonValue>(value.Count);
                    foreach (JsonValue item in value.Items)
                    {
                        items.Add(Inline(item));
                    }

                    return JsonValue.Array(items);
                }

                if (value.IsObject)
                {
                    string reference = value["$ref"].AsString(null);
                    if (reference != null)
                    {
                        JsonValue target = ResolveReference(reference);
                        JsonValue merged = DeepCopy(target);

                        // Sibling keywords (e.g. "default") win over the target's own.
                        if (merged.IsObject)
                        {
                            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                            foreach (var member in merged.Members)
                            {
                                members[member.Key] = Inline(member.Value);
                            }

                            foreach (var member in value.Members)
                            {
                                if (member.Key != "$ref")
                                {
                                    members[member.Key] = Inline(member.Value);
                                }
                            }

                            merged = JsonValue.Object(members);
                        }

                        return merged;
                    }

                    var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                    foreach (var member in value.Members)
                    {
                        result[member.Key] = Inline(member.Value);
                    }

                    return JsonValue.Object(result);
                }

                return value;
            }

            private JsonValue ResolveReference(string reference)
            {
                string file = null;
                string fragment = null;
                int hash = reference.IndexOf('#');
                if (hash >= 0)
                {
                    file = reference.Substring(0, hash);
                    fragment = reference.Substring(hash + 1);
                }
                else
                {
                    file = reference;
                }

                JsonValue root = _root;
                if (!string.IsNullOrEmpty(file))
                {
                    string name = file.Replace('\\', '/');
                    int slash = name.LastIndexOf('/');
                    if (slash >= 0)
                    {
                        name = name.Substring(slash + 1);
                    }

                    if (name.EndsWith(".schema.json", StringComparison.Ordinal) && !_loading.Contains(name))
                    {
                        _loading.Add(name);
                        try
                        {
                            // A $ref may point at a root-level schema (element-reference,
                            // pipe-envelope) or a schema inside operations/ (wall.create).
                            // Probe both resource names; the first hit wins.
                            string rootResource = "AutodeskNativeAgent.Core.Schemas." + name;
                            string operationResource = "AutodeskNativeAgent.Core.Schemas.operations\\" + name;
                            JsonValue external;
                            if (ResourceExists(typeof(SchemaCatalog).Assembly, operationResource))
                            {
                                external = EmbeddedSchemas.Load(typeof(SchemaCatalog).Assembly, operationResource);
                            }
                            else
                            {
                                external = EmbeddedSchemas.Load(typeof(SchemaCatalog).Assembly, rootResource);
                            }

                            root = new RefResolver(external).Resolve();
                        }
                        finally
                        {
                            _loading.Remove(name);
                        }
                    }
                }

                if (string.IsNullOrEmpty(fragment) || fragment == "/")
                {
                    return root;
                }

                string[] parts = fragment.Split('/');
                JsonValue current = root;
                foreach (string part in parts)
                {
                    if (string.IsNullOrEmpty(part))
                    {
                        continue;
                    }

                    current = current[part];
                    if (current.IsNull)
                    {
                        break;
                    }
                }

                return current;
            }

            private static bool ResourceExists(System.Reflection.Assembly assembly, string resourceName)
            {
                return assembly.GetManifestResourceInfo(resourceName) != null;
            }
        }
    }
}
