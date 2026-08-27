using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>Direction of a protocol method.</summary>
    public enum MethodDirection
    {
        /// <summary>Client (MCP server) asks the add-in.</summary>
        Request = 0,

        /// <summary>Add-in notifies the client without being asked (heartbeat, status).</summary>
        Notification = 1
    }

    /// <summary>
    /// Category of a protocol method, for capabilities reporting and allowlisting.
    /// </summary>
    public enum MethodCategory
    {
        /// <summary>Connection lifecycle: hello, heartbeat, status.</summary>
        Connection = 0,

        /// <summary>Document / model inspection: get_info, get_units, lists.</summary>
        Read = 1,

        /// <summary>Plan lifecycle: preview, commit, job status, cancel.</summary>
        Plan = 2,

        /// <summary>Selection and view inspection.</summary>
        Selection = 3,

        /// <summary>Destructive or state-changing safety flows.</summary>
        Safety = 4
    }

    /// <summary>Describes one protocol method for capabilities and documentation.</summary>
    public sealed class ProtocolMethod
    {
        /// <summary>Creates a method descriptor.</summary>
        public ProtocolMethod(
            string name,
            MethodDirection direction,
            MethodCategory category,
            string summary,
            bool supported = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Direction = direction;
            Category = category;
            Summary = summary ?? string.Empty;
            Supported = supported;
        }

        /// <summary>Wire method name, e.g. "plan.commit".</summary>
        public string Name { get; }

        /// <summary>Client-to-addin request or addin-to-client notification.</summary>
        public MethodDirection Direction { get; }

        /// <summary>Functional category.</summary>
        public MethodCategory Category { get; }

        /// <summary>One-line description surfaced to agents.</summary>
        public string Summary { get; }

        /// <summary>False for methods reserved by the schema but not implemented.</summary>
        public bool Supported { get; }

        /// <summary>Serialises to the wire form (used by revit_get_capabilities).</summary>
        public JsonValue ToJson()
        {
            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["name"] = JsonValue.String(Name),
                ["direction"] = JsonValue.String(Direction == MethodDirection.Notification ? "notification" : "request"),
                ["category"] = JsonValue.String(CategoryToWire(Category)),
                ["summary"] = JsonValue.String(Summary),
                ["supported"] = JsonValue.Bool(Supported)
            });
        }

        private static string CategoryToWire(MethodCategory category)
        {
            switch (category)
            {
                case MethodCategory.Read: return "read";
                case MethodCategory.Plan: return "plan";
                case MethodCategory.Selection: return "selection";
                case MethodCategory.Safety: return "safety";
                default: return "connection";
            }
        }
    }

    /// <summary>
    /// Static catalog of every protocol method the MCP server and add-in may exchange.
    /// Names are the wire contract: they must never be reworded.
    /// </summary>
    public static class ProtocolCatalog
    {
        // --- Connection lifecycle ---
        public const string Hello = "hello";
        public const string Status = "status";
        public const string Capabilities = "capabilities";

        // --- Document / model inspection ---
        public const string DocumentGetInfo = "document.get_info";
        public const string DocumentGetUnits = "document.get_units";
        public const string DocumentGetActiveView = "document.get_active_view";
        public const string LevelList = "level.list";
        public const string ViewList = "view.list";
        public const string SheetList = "sheet.list";
        public const string FamilyList = "family.list";
        public const string FamilyTypeList = "family_type.list";
        public const string CategoryList = "category.list";
        public const string WorksetList = "workset.list";

        // --- Elements ---
        public const string SelectionGet = "selection.get";
        public const string ElementGet = "element.get";
        public const string ElementQuery = "element.query";
        public const string ElementGetParameters = "element.get_parameters";
        public const string ElementGetBoundingBox = "element.get_bounding_box";
        public const string ElementGetLocation = "element.get_location";

        // --- Plan lifecycle ---
        public const string PlanValidate = "plan.validate";
        public const string PlanPreview = "plan.preview";
        public const string PlanCommit = "plan.commit";
        public const string PlanConfirm = "plan.confirm";
        public const string JobStatus = "job.status";
        public const string JobCancel = "job.cancel";
        public const string JobRollback = "job.rollback";
        public const string AuditLog = "audit.log";

        /// <summary>All supported methods, ordered by name.</summary>
        public static IReadOnlyList<ProtocolMethod> All { get; } = BuildAll();

        /// <summary>Finds a method by name, or null.</summary>
        public static ProtocolMethod Find(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (ProtocolMethod method in All)
            {
                if (string.Equals(method.Name, name, StringComparison.Ordinal))
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>True when the method is in the catalog and marked supported.</summary>
        public static bool IsSupported(string name)
        {
            ProtocolMethod method = Find(name);
            return method != null && method.Supported;
        }

        private static IReadOnlyList<ProtocolMethod> BuildAll()
        {
            var list = new List<ProtocolMethod>
            {
                new ProtocolMethod(Hello, MethodDirection.Request, MethodCategory.Connection, "Handshake; negotiates the protocol version."),
                new ProtocolMethod(Status, MethodDirection.Request, MethodCategory.Connection, "Returns connection status, active document title and path, and busy state."),
                new ProtocolMethod(Capabilities, MethodDirection.Request, MethodCategory.Connection, "Returns the method/operation allowlist and safety limits."),
                new ProtocolMethod(DocumentGetInfo, MethodDirection.Request, MethodCategory.Read, "Returns identity of the active document: title, path, project info, fingerprint."),
                new ProtocolMethod(DocumentGetUnits, MethodDirection.Request, MethodCategory.Read, "Returns the document's display unit system."),
                new ProtocolMethod(DocumentGetActiveView, MethodDirection.Request, MethodCategory.Read, "Returns the active view id, name and level."),
                new ProtocolMethod(LevelList, MethodDirection.Request, MethodCategory.Read, "Lists levels with id, name, elevation and uniqueId."),
                new ProtocolMethod(ViewList, MethodDirection.Request, MethodCategory.Read, "Lists views with id, name, kind and template binding."),
                new ProtocolMethod(SheetList, MethodDirection.Request, MethodCategory.Read, "Lists sheets with id, number and name."),
                new ProtocolMethod(FamilyList, MethodDirection.Request, MethodCategory.Read, "Lists loaded families for a category."),
                new ProtocolMethod(FamilyTypeList, MethodDirection.Request, MethodCategory.Read, "Lists family symbols for a family/category."),
                new ProtocolMethod(CategoryList, MethodDirection.Request, MethodCategory.Read, "Lists categories available in the document."),
                new ProtocolMethod(WorksetList, MethodDirection.Request, MethodCategory.Read, "Lists worksets with id, name and visibility."),
                new ProtocolMethod(SelectionGet, MethodDirection.Request, MethodCategory.Selection, "Returns the current selection as element summaries."),
                new ProtocolMethod(ElementGet, MethodDirection.Request, MethodCategory.Read, "Returns a single element summary by uniqueId/elementId/query."),
                new ProtocolMethod(ElementQuery, MethodDirection.Request, MethodCategory.Read, "Runs an allowlisted query; returns matching element summaries."),
                new ProtocolMethod(ElementGetParameters, MethodDirection.Request, MethodCategory.Read, "Returns all readable parameters of an element."),
                new ProtocolMethod(ElementGetBoundingBox, MethodDirection.Request, MethodCategory.Read, "Returns the bounding box of an element in plan units."),
                new ProtocolMethod(ElementGetLocation, MethodDirection.Request, MethodCategory.Read, "Returns the location point/curve of an element in plan units."),
                new ProtocolMethod(PlanValidate, MethodDirection.Request, MethodCategory.Plan, "Validates a plan structurally and against the command allowlist."),
                new ProtocolMethod(PlanPreview, MethodDirection.Request, MethodCategory.Plan, "Dry-runs a plan: resolution only, never mutates the model."),
                new ProtocolMethod(PlanCommit, MethodDirection.Request, MethodCategory.Plan, "Executes a plan inside a transaction group after confirmation."),
                new ProtocolMethod(PlanConfirm, MethodDirection.Request, MethodCategory.Plan, "Accepts/rejects a pending confirmation token."),
                new ProtocolMethod(JobStatus, MethodDirection.Request, MethodCategory.Plan, "Returns the current job status and latest execution result."),
                new ProtocolMethod(JobCancel, MethodDirection.Request, MethodCategory.Plan, "Requests cancellation of a queued or running job."),
                new ProtocolMethod(JobRollback, MethodDirection.Request, MethodCategory.Safety, "Rolls back a completed job when the runtime supports it."),
                new ProtocolMethod(AuditLog, MethodDirection.Request, MethodCategory.Safety, "Returns sanitised audit log entries."),
            };

            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }
    }
}
