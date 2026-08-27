using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>
    /// A stable reference to a Revit element, mirroring element-reference.schema.json.
    /// UniqueId is the strongest signal; ElementId alone is never sufficient.
    /// </summary>
    /// <remarks>
    /// The wire form is intentionally a plain object with one of
    /// <c>uniqueId</c>, <c>elementId</c> or <c>viaOperationResult</c> set.
    /// <c>viaOperationResult</c> is resolved by the ResultReferenceResolver at
    /// execution time against earlier operation outcomes.
    /// </remarks>
    public sealed class ElementReference
    {
        /// <summary>Creates an element reference.</summary>
        public ElementReference(
            string uniqueId = null,
            long elementId = 0,
            string viaOperationResult = null,
            string documentFingerprint = null,
            string category = null,
            string expectedName = null,
            string expectedTypeName = null)
        {
            UniqueId = uniqueId;
            ElementId = elementId;
            ViaOperationResult = viaOperationResult;
            DocumentFingerprint = documentFingerprint;
            Category = category;
            ExpectedName = expectedName;
            ExpectedTypeName = expectedTypeName;
        }

        /// <summary>Stable UniqueId of the element.</summary>
        public string UniqueId { get; }

        /// <summary>Revit ElementId (long on the wire; Revit 2024 exposes int32).</summary>
        public long ElementId { get; }

        /// <summary><c>$result.&lt;opId&gt;</c> reference resolved during execution.</summary>
        public string ViaOperationResult { get; }

        /// <summary>Fingerprint of the document the reference was captured in.</summary>
        public string DocumentFingerprint { get; }

        /// <summary>Expected Revit category name, e.g. "Walls".</summary>
        public string Category { get; }

        /// <summary>Expected element name, used for stale-reference detection.</summary>
        public string ExpectedName { get; }

        /// <summary>Expected type name, used for stale-reference detection.</summary>
        public string ExpectedTypeName { get; }

        /// <summary>True when the reference carries at least one usable key.</summary>
        public bool HasUsableKey =>
            !string.IsNullOrEmpty(UniqueId) || ElementId > 0 || !string.IsNullOrEmpty(ViaOperationResult);

        /// <summary>Parses from the wire form. Returns null for non-objects.</summary>
        public static ElementReference FromJson(JsonValue json)
        {
            if (json == null || !json.IsObject)
            {
                return null;
            }

            return new ElementReference(
                json["uniqueId"].AsString(null),
                json["elementId"].AsLong(0),
                json["viaOperationResult"].AsString(json["operationResult"].AsString(null)),
                json["documentFingerprint"].AsString(null),
                json["category"].AsString(null),
                json["expectedName"].AsString(null),
                json["expectedTypeName"].AsString(null));
        }

        /// <summary>Serialises to the wire form.</summary>
        public JsonValue ToJson()
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(DocumentFingerprint))
            {
                members["documentFingerprint"] = JsonValue.String(DocumentFingerprint);
            }

            if (!string.IsNullOrEmpty(UniqueId))
            {
                members["uniqueId"] = JsonValue.String(UniqueId);
            }

            if (ElementId > 0)
            {
                members["elementId"] = JsonValue.Number(ElementId);
            }

            if (!string.IsNullOrEmpty(ViaOperationResult))
            {
                members["viaOperationResult"] = JsonValue.String(ViaOperationResult);
            }

            if (!string.IsNullOrEmpty(Category))
            {
                members["category"] = JsonValue.String(Category);
            }

            if (!string.IsNullOrEmpty(ExpectedName))
            {
                members["expectedName"] = JsonValue.String(ExpectedName);
            }

            if (!string.IsNullOrEmpty(ExpectedTypeName))
            {
                members["expectedTypeName"] = JsonValue.String(ExpectedTypeName);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Human-readable description, for logs and audit entries.</summary>
        public override string ToString()
        {
            if (!string.IsNullOrEmpty(UniqueId))
            {
                return "uniqueId:" + UniqueId;
            }

            if (ElementId > 0)
            {
                return "elementId:" + ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(ViaOperationResult))
            {
                return "result:" + ViaOperationResult;
            }

            return "<empty element reference>";
        }
    }
}
