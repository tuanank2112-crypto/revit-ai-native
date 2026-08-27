using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Resolves element references (elementId/uniqueId/$result chains) and host elements for
    /// operations like door.insert. Host strategy: a wall by uniqueId/elementId, or the wall
    /// produced by an earlier operation through <c>$result</c>.
    /// </summary>
    public static class ElementResolver
    {
        /// <summary>Resolves a single element from a reference JSON.</summary>
        public static Element Resolve(Document document, JsonValue reference, IReadOnlyDictionary<string, JsonValue> results)
        {
            if (reference == null || !reference.IsObject)
            {
                throw new AgentException(ErrorCodes.UnresolvedReference, "Element reference must be an object.", true);
            }

            string viaResult = reference["viaOperationResult"].AsString(null);
            if (viaResult != null)
            {
                JsonValue resolved;
                string error;
                var resolver = new AutodeskNativeAgent.Core.Execution.ResultReferenceResolver(results);
                if (!resolver.TryResolve(viaResult, out resolved, out error))
                {
                    throw new AgentException(ErrorCodes.UnresolvedReference, "Cannot resolve '" + viaResult + "': " + error, true);
                }

                return ResolveResolvedJson(document, resolved, viaResult);
            }

            string operationResult = reference["operationResult"].AsString(null);
            if (operationResult != null)
            {
                JsonValue resolved;
                string error;
                var resolver = new AutodeskNativeAgent.Core.Execution.ResultReferenceResolver(results);
                if (!resolver.TryResolve(operationResult, out resolved, out error))
                {
                    throw new AgentException(ErrorCodes.UnresolvedReference, "Cannot resolve '" + operationResult + "': " + error, true);
                }

                return ResolveResolvedJson(document, resolved, operationResult);
            }

            string uniqueId = reference["uniqueId"].AsString(null);
            if (!string.IsNullOrEmpty(uniqueId))
            {
                Element element = null;
                try
                {
                    element = document.GetElement(uniqueId);
                }
                catch
                {
                    element = null;
                }

                if (element == null)
                {
                    throw new AgentException(ErrorCodes.ElementNotFound,
                        "No element with uniqueId '" + uniqueId + "' in the current document.", true);
                }

                return element;
            }

            long elementId = reference["elementId"].AsLong(0);
            if (elementId > 0)
            {
                Element element = document.GetElement(new ElementId(elementId));
                if (element == null)
                {
                    throw new AgentException(ErrorCodes.ElementNotFound,
                        "No element with Id " + elementId + " in the current document.", true);
                }

                return element;
            }

            throw new AgentException(ErrorCodes.UnresolvedReference,
                "Element reference carries no usable key (need uniqueId, elementId, or operationResult).", true);
        }

        private static Element ResolveResolvedJson(Document document, JsonValue resolved, string origin)
        {
            // A bare "$result.<opId>" reference resolves to the whole OperationResult JSON,
            // which has no top-level uniqueId/elementId. Treat it as a shorthand for the
            // first created element (per ResultReferenceResolver docs) when present.
            if (resolved.IsObject)
            {
                JsonValue created = resolved["createdElements"];
                if (created.IsArray && created.Count > 0)
                {
                    resolved = created[0];
                }
            }

            // Prefer elementId: Document.GetElement(ElementId) is reliable inside the same
            // transaction for freshly created elements.
            long elementId = resolved["elementId"].AsLong(resolved["id"].AsLong(0));
            if (elementId > 0)
            {
                try
                {
                    Element element = document.GetElement(new ElementId(elementId));
                    if (element != null)
                    {
                        return element;
                    }
                }
                catch
                {
                    // fall through to uniqueId
                }
            }

            string uniqueId = resolved["uniqueId"].AsString(null);
            if (!string.IsNullOrEmpty(uniqueId))
            {
                try
                {
                    Element element = document.GetElement(uniqueId);
                    if (element != null)
                    {
                        return element;
                    }
                }
                catch
                {
                    // fall through
                }
            }

            throw new AgentException(ErrorCodes.UnresolvedReference,
                "Resolved reference '" + origin + "' does not identify an element.", true);
        }

        /// <summary>Resolves a wall host from the door.insert "host" object.</summary>
        public static Wall ResolveWallHost(Document document, JsonValue hostJson, IReadOnlyDictionary<string, JsonValue> results)
        {
            Element element = Resolve(document, hostJson, results);
            var wall = element as Wall;
            if (wall == null)
            {
                throw new AgentException(ErrorCodes.InvalidHost,
                    "Host is not a wall (was '" + (element != null ? element.Category?.Name : "<none>") + "').", true);
            }

            return wall;
        }

        /// <summary>Creates an ElementSummary snapshot for a resolved element.</summary>
        public static ElementSummary Summarize(Element element)
        {
            if (element == null)
            {
                return null;
            }

            string category = element.Category != null ? element.Category.Name : string.Empty;
            string name = null;
            try
            {
                name = element.Name;
            }
            catch
            {
                name = null;
            }

            return new ElementSummary(
                element.Id.Value,
                element.UniqueId,
                category,
                name,
                GetTypeName(element));
        }

        /// <summary>Creates an ElementSummary from a Revit elementId (safe even when the element is gone).</summary>
        public static ElementSummary SnapshotById(Document document, ElementId id)
        {
            Element element = document.GetElement(id);
            return element != null ? Summarize(element) : null;
        }

        private static string GetTypeName(Element element)
        {
            ElementType type = element.Document.GetElement(element.GetTypeId()) as ElementType;
            return type != null ? type.Name : null;
        }
    }
}
