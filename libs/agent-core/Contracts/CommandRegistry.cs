using System;
using System.Collections.Generic;
using System.Reflection;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Validation;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>
    /// The command registry allowlist. Only operation names registered here can appear in a
    /// plan; everything else is rejected with <see cref="ErrorCodes.UnknownOperation"/>.
    /// </summary>
    /// <remarks>
    /// Affected-element accounting is a registry concern, not a per-operation concern: it is
    /// deliberately part of the same allowlist so that a newly added operation cannot forget
    /// to declare how many elements it may touch.
    /// </remarks>
    public sealed class OperationDescriptor
    {
        /// <summary>Creates an operation descriptor.</summary>
        public OperationDescriptor(
            string op,
            int creates,
            int modifies,
            int deletes,
            string summary,
            JsonValue argumentSchema,
            bool supportedInPreview = true,
            bool supportedInCommit = true)
        {
            Op = op;
            Creates = creates;
            Modifies = modifies;
            Deletes = deletes;
            Summary = summary;
            ArgumentSchema = argumentSchema ?? JsonValue.Null;
            SupportedInPreview = supportedInPreview;
            SupportedInCommit = supportedInCommit;
        }

        /// <summary>Operation name, e.g. wall.create.</summary>
        public string Op { get; }

        /// <summary>Upper bound of elements the operation creates.</summary>
        public int Creates { get; }

        /// <summary>Upper bound of elements the operation modifies.</summary>
        public int Modifies { get; }

        /// <summary>Upper bound of elements the operation deletes.</summary>
        public int Deletes { get; }

        /// <summary>One-line description surfaced to agents.</summary>
        public string Summary { get; }

        /// <summary>Per-operation argument schema, loaded from the embedded contracts.</summary>
        public JsonValue ArgumentSchema { get; }

        /// <summary>Whether the operation can run in dry-run preview.</summary>
        public bool SupportedInPreview { get; }

        /// <summary>Whether the operation can be committed.</summary>
        public bool SupportedInCommit { get; }

        /// <summary>Total affected-element ceiling for the operation.</summary>
        public int MaxAffected => Creates + Modifies + Deletes;

        /// <summary>Validates the raw operation arguments against the per-op schema.</summary>
        public List<string> ValidateArguments(JsonValue args)
        {
            if (ArgumentSchema.IsNull)
            {
                return new List<string>();
            }

            return JsonSchemaValidator.Validate(args ?? JsonValue.EmptyObject(), ArgumentSchema);
        }
    }

    /// <summary>Loads an embedded JSON schema document.</summary>
    public static class EmbeddedSchemas
    {
        /// <summary>Reads an embedded resource as a parsed JsonValue.</summary>
        public static JsonValue Load(Assembly assembly, string resourceName)
        {
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new SchemaException("Embedded schema '" + resourceName + "' was not found.");
                }

                using (var reader = new System.IO.StreamReader(stream))
                {
                    string text = reader.ReadToEnd();
                    JsonValue json;
                    string error;
                    if (!JsonParser.TryParse(text, out json, out error))
                    {
                        throw new SchemaException("Embedded schema '" + resourceName + "' is not valid JSON: " + error);
                    }

                    return json;
                }
            }
        }
    }

    /// <summary>
    /// The allowlist of operations an agent may put in a plan, together with their argument
    /// schemas and affecting metadata.
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, OperationDescriptor> _registry =
            new Dictionary<string, OperationDescriptor>(StringComparer.Ordinal);

        /// <summary>Shared registry with all built-in operations.</summary>
        public static CommandRegistry CreateDefault()
        {
            var registry = new CommandRegistry();

            registry.Register(new OperationDescriptor(
                "wall.create",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Creates a straight wall from start to end, on the given level, using the resolved type.",
                argumentSchema: SchemaCatalog.LoadOperationSchema("wall.create")));

            registry.Register(new OperationDescriptor(
                "wall.update",
                creates: 0,
                modifies: 1,
                deletes: 0,
                summary: "Updates properties of an existing wall (type, height, offsets, structural).",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "door.insert",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Inserts a door family instance hosted by a wall at the requested location.",
                argumentSchema: SchemaCatalog.LoadOperationSchema("door.insert")));

            registry.Register(new OperationDescriptor(
                "window.insert",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Inserts a window family instance hosted by a wall at the requested location.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "parameter.set",
                creates: 0,
                modifies: 1,
                deletes: 0,
                summary: "Sets a typed parameter value on a single element (instance or type scope).",
                argumentSchema: SchemaCatalog.LoadOperationSchema("parameter.set")));

            registry.Register(new OperationDescriptor(
                "parameter.set_many",
                creates: 0,
                modifies: -1,
                deletes: 0,
                summary: "Sets multiple typed parameters on one or more elements atomically.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "element.delete",
                creates: 0,
                modifies: 0,
                deletes: 1,
                summary: "Deletes a single element referenced by UniqueId, ElementId or a previous operation result.",
                argumentSchema: SchemaCatalog.LoadOperationSchema("element.delete")));

            registry.Register(new OperationDescriptor(
                "element.move",
                creates: 0,
                modifies: 1,
                deletes: 0,
                summary: "Translates an element by a vector in plan units.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "element.rotate",
                creates: 0,
                modifies: 1,
                deletes: 0,
                summary: "Rotates an element around an axis by a specified angle.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "element.rename",
                creates: 0,
                modifies: 1,
                deletes: 0,
                summary: "Changes the Name property of an element.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "room.create",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Creates a room on a level at a given UV point.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "view.create_plan",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Creates a floor plan or ceiling plan view for a level.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "sheet.create",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Creates a new sheet with an optional title block.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "sheet.place_view",
                creates: 1,
                modifies: 0,
                deletes: 0,
                summary: "Places a view on a sheet at a specified location.",
                argumentSchema: JsonValue.Null));

            registry.Register(new OperationDescriptor(
                "document.save",
                creates: 0,
                modifies: 0,
                deletes: 0,
                summary: "Saves the active document to its current path.",
                argumentSchema: JsonValue.Null,
                supportedInPreview: false));

            registry.Register(new OperationDescriptor(
                "document.save_as",
                creates: 0,
                modifies: 0,
                deletes: 0,
                summary: "Saves the active document to a new path (sensitive).",
                argumentSchema: JsonValue.Null,
                supportedInPreview: false));

            registry.Register(new OperationDescriptor(
                "export.dwg",
                creates: 0,
                modifies: 0,
                deletes: 0,
                summary: "Exports selected views/sheets to DWG.",
                argumentSchema: JsonValue.Null,
                supportedInPreview: false));

            registry.Register(new OperationDescriptor(
                "export.pdf",
                creates: 0,
                modifies: 0,
                deletes: 0,
                summary: "Exports selected views/sheets to PDF.",
                argumentSchema: JsonValue.Null,
                supportedInPreview: false));

            return registry;
        }

        /// <summary>Registers an operation. Duplicate names are rejected.</summary>
        public void Register(OperationDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (_registry.ContainsKey(descriptor.Op))
            {
                throw new InvalidOperationException("Duplicate operation registration '" + descriptor.Op + "'.");
            }

            _registry.Add(descriptor.Op, descriptor);
        }

        /// <summary>Returns true when the operation is allowlisted.</summary>
        public bool IsKnown(string op) => op != null && _registry.ContainsKey(op);

        /// <summary>Gets the descriptor, or null when unknown.</summary>
        public OperationDescriptor Find(string op)
        {
            if (op == null)
            {
                return null;
            }

            OperationDescriptor descriptor;
            return _registry.TryGetValue(op, out descriptor) ? descriptor : null;
        }

        /// <summary>All registered operations, ordered by name.</summary>
        public IReadOnlyList<OperationDescriptor> All
        {
            get
            {
                var list = new List<OperationDescriptor>(_registry.Values);
                list.Sort((a, b) => string.CompareOrdinal(a.Op, b.Op));
                return list;
            }
        }
    }
}
