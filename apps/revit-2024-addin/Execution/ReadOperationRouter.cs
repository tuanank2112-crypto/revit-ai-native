using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Routes read operations to their handlers. Every operation is dispatched through a
    /// strongly-typed switch — no reflection or dynamic dispatch. Unknown operations
    /// return UNSUPPORTED_OPERATION.
    /// </summary>
    public static class ReadOperationRouter
    {
        /// <summary>Handles a read operation. Must be called on the main thread.</summary>
        public static JsonValue Handle(UIApplication app, Document document, string method, JsonValue payload)
        {
            switch (method)
            {
                case ProtocolCatalog.DocumentGetInfo:
                    return GetDocumentInfo(document);

                case ProtocolCatalog.DocumentGetUnits:
                    return GetDocumentUnits(document);

                case ProtocolCatalog.DocumentGetActiveView:
                    return GetActiveView(document);

                case ProtocolCatalog.SelectionGet:
                    return GetSelection(app);

                case ProtocolCatalog.LevelList:
                    return ListLevels(document);

                case ProtocolCatalog.ViewList:
                    return ListViews(document);

                case ProtocolCatalog.SheetList:
                    return ListSheets(document);

                case ProtocolCatalog.FamilyList:
                    return ListFamilies(document, payload);

                case ProtocolCatalog.FamilyTypeList:
                    return ListFamilyTypes(document, payload);

                case ProtocolCatalog.CategoryList:
                    return ListCategories(document);

                case ProtocolCatalog.WorksetList:
                    return ListWorksets(document);

                case ProtocolCatalog.ElementGet:
                    return GetElement(document, payload);

                case ProtocolCatalog.ElementQuery:
                    return QueryElements(document, payload);

                case ProtocolCatalog.ElementGetParameters:
                    return GetElementParameters(document, payload);

                case ProtocolCatalog.ElementGetBoundingBox:
                    return GetElementBoundingBox(document, payload);

                case ProtocolCatalog.ElementGetLocation:
                    return GetElementLocation(document, payload);

                default:
                    throw new AgentException(ErrorCodes.OperationNotSupported,
                        "Read operation '" + method + "' is not supported.", false);
            }
        }

        // --- Document operations ---

        private static JsonValue GetDocumentInfo(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            string title = SafeString(() => document.Title);
            string path = SafeString(() => document.PathName);
            string projectNumber = SafeString(() => document.ProjectInformation?.Number);
            string projectName = SafeString(() => document.ProjectInformation?.Name);

            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["title"] = JsonValue.String(title),
                ["path"] = JsonValue.String(path),
                ["projectNumber"] = JsonValue.String(projectNumber),
                ["projectName"] = JsonValue.String(projectName),
                ["isReadOnly"] = JsonValue.Bool(document.IsReadOnly),
                ["isModifiable"] = JsonValue.Bool(document.IsModifiable),
                ["isWorkshared"] = JsonValue.Bool(document.IsWorkshared),
                ["fingerprint"] = JsonValue.String(
                    Core.Identity.DocumentFingerprint.FromIdentity(title, path, projectNumber, projectName))
            };

            return JsonValue.Object(members);
        }

        private static JsonValue GetDocumentUnits(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            // Revit 2024 uses UnitUtils with ForgeTypeId spec ids.
            var units = document.GetUnits();
            var lengthFormat = units.GetFormatOptions(SpecTypeId.Length);
            var areaFormat = units.GetFormatOptions(SpecTypeId.Area);

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["lengthDisplayUnit"] = JsonValue.String(lengthFormat == null ? string.Empty : "internal"),
                ["areaDisplayUnit"] = JsonValue.String(areaFormat == null ? string.Empty : "internal"),
                ["lengthSpecId"] = JsonValue.String(SpecTypeId.Length.TypeId),
                ["areaSpecId"] = JsonValue.String(SpecTypeId.Area.TypeId)
            });
        }

        private static JsonValue GetActiveView(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            View view = document.ActiveView;
            if (view == null)
            {
                return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["activeView"] = JsonValue.Null
                });
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["id"] = JsonValue.Number(view.Id.Value),
                ["uniqueId"] = JsonValue.String(view.UniqueId),
                ["name"] = JsonValue.String(view.Name ?? string.Empty),
                ["viewType"] = JsonValue.String(view.ViewType.ToString()),
                ["isTemplate"] = JsonValue.Bool(view.IsTemplate)
            });
        }

        // --- Selection ---

        private static JsonValue GetSelection(UIApplication app)
        {
            if (app == null || app.ActiveUIDocument == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var uidoc = app.ActiveUIDocument;
            Document document = uidoc.Document;
            string title = SafeString(() => document.Title);
            string path = SafeString(() => document.PathName);
            string projectNumber = SafeString(() => document.ProjectInformation?.Number);
            string projectName = SafeString(() => document.ProjectInformation?.Name);
            string fingerprint = Core.Identity.DocumentFingerprint.FromIdentity(
                title, path, projectNumber, projectName);

            var items = new List<JsonValue>();
            var references = new List<JsonValue>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                Element element = document.GetElement(id);
                if (element != null)
                {
                    ElementSummary summary = ElementResolver.Summarize(element);
                    items.Add(summary.ToJson());
                    references.Add(new ElementReference(
                        uniqueId: summary.UniqueId,
                        elementId: summary.ElementId,
                        documentFingerprint: fingerprint,
                        category: summary.Category,
                        expectedName: summary.Name,
                        expectedTypeName: summary.TypeName).ToJson());
                }
            }

            View activeView = document.ActiveView;
            JsonValue activeViewJson = activeView == null
                ? JsonValue.Null
                : JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(activeView.Id.Value),
                    ["uniqueId"] = JsonValue.String(activeView.UniqueId),
                    ["name"] = JsonValue.String(activeView.Name ?? string.Empty),
                    ["viewType"] = JsonValue.String(activeView.ViewType.ToString()),
                    ["isTemplate"] = JsonValue.Bool(activeView.IsTemplate)
                });

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["documentFingerprint"] = JsonValue.String(fingerprint),
                ["activeView"] = activeViewJson,
                ["elements"] = JsonValue.Array(items),
                ["references"] = JsonValue.Array(references),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        // --- List operations ---

        private static JsonValue ListLevels(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();
            var collector = new FilteredElementCollector(document).OfClass(typeof(Level));
            foreach (Level level in collector)
            {
                items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(level.Id.Value),
                    ["uniqueId"] = JsonValue.String(level.UniqueId),
                    ["name"] = JsonValue.String(level.Name ?? string.Empty),
                    ["elevation"] = JsonValue.Number(level.Elevation)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["levels"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static JsonValue ListViews(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();
            var collector = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType();

            foreach (View view in collector)
            {
                if (view.IsTemplate)
                {
                    continue;
                }

                items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(view.Id.Value),
                    ["uniqueId"] = JsonValue.String(view.UniqueId),
                    ["name"] = JsonValue.String(view.Name ?? string.Empty),
                    ["viewType"] = JsonValue.String(view.ViewType.ToString())
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["views"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static JsonValue ListSheets(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();
            var collector = new FilteredElementCollector(document).OfClass(typeof(ViewSheet));
            foreach (ViewSheet sheet in collector)
            {
                items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(sheet.Id.Value),
                    ["uniqueId"] = JsonValue.String(sheet.UniqueId),
                    ["sheetNumber"] = JsonValue.String(sheet.SheetNumber ?? string.Empty),
                    ["name"] = JsonValue.String(sheet.Name ?? string.Empty)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["sheets"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static JsonValue ListFamilies(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();
            var collector = new FilteredElementCollector(document).OfClass(typeof(Family));

            foreach (Family family in collector)
            {
                items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(family.Id.Value),
                    ["uniqueId"] = JsonValue.String(family.UniqueId),
                    ["name"] = JsonValue.String(family.Name ?? string.Empty),
                    ["category"] = JsonValue.String(family.Category != null ? family.Category.Name : string.Empty)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["families"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static JsonValue ListFamilyTypes(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();
            var collector = new FilteredElementCollector(document).OfClass(typeof(FamilySymbol));

            foreach (FamilySymbol symbol in collector)
            {
                items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(symbol.Id.Value),
                    ["uniqueId"] = JsonValue.String(symbol.UniqueId),
                    ["familyName"] = JsonValue.String(symbol.FamilyName ?? string.Empty),
                    ["typeName"] = JsonValue.String(symbol.Name ?? string.Empty),
                    ["category"] = JsonValue.String(symbol.Category != null ? symbol.Category.Name : string.Empty)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["familyTypes"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static JsonValue ListCategories(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();
            foreach (Category cat in document.Settings.Categories)
            {
                items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["id"] = JsonValue.Number(cat.Id.Value),
                    ["name"] = JsonValue.String(cat.Name ?? string.Empty),
                    ["allowsBoundParameters"] = JsonValue.Bool(cat.AllowsBoundParameters)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["categories"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static JsonValue ListWorksets(Document document)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            var items = new List<JsonValue>();

            if (document.IsWorkshared)
            {
                var worksets = new FilteredWorksetCollector(document).ToWorksets();
                foreach (Workset ws in worksets)
                {
                    items.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(ws.Id.IntegerValue),
                        ["name"] = JsonValue.String(ws.Name ?? string.Empty),
                        ["kind"] = JsonValue.String(ws.Kind.ToString())
                    }));
                }
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["worksets"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        // --- Element operations ---

        private static JsonValue GetElement(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            Element element = ElementResolver.Resolve(document, payload, new Dictionary<string, JsonValue>(StringComparer.Ordinal));
            return ElementResolver.Summarize(element).ToJson();
        }

        private static JsonValue QueryElements(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            return ElementQueryEngine.Execute(document, payload);
        }

        private static JsonValue GetElementParameters(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            Element element = ElementResolver.Resolve(document, payload, new Dictionary<string, JsonValue>(StringComparer.Ordinal));
            var params_ = new List<JsonValue>();

            foreach (Parameter p in element.Parameters)
            {
                params_.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["name"] = JsonValue.String(p.Definition?.Name ?? string.Empty),
                    ["value"] = JsonValue.String(ParameterToString(p)),
                    ["storageType"] = JsonValue.String(p.StorageType.ToString()),
                    ["isReadOnly"] = JsonValue.Bool(p.IsReadOnly),
                    ["unit"] = JsonValue.String(p.Definition?.GetDataType()?.TypeId ?? string.Empty)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["parameters"] = JsonValue.Array(params_),
                ["count"] = JsonValue.Number(params_.Count)
            });
        }

        private static JsonValue GetElementBoundingBox(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            Element element = ElementResolver.Resolve(document, payload, new Dictionary<string, JsonValue>(StringComparer.Ordinal));
            BoundingBoxXYZ bbox = element.get_BoundingBox(null);
            if (bbox == null)
            {
                return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["boundingBox"] = JsonValue.Null,
                    ["reason"] = JsonValue.String("Element has no bounding box.")
                });
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["min"] = XyzToObject(bbox.Min),
                ["max"] = XyzToObject(bbox.Max)
            });
        }

        private static JsonValue GetElementLocation(Document document, JsonValue payload)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            Element element = ElementResolver.Resolve(document, payload, new Dictionary<string, JsonValue>(StringComparer.Ordinal));
            var location = element.Location;

            if (location is LocationPoint point)
            {
                return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["kind"] = JsonValue.String("point"),
                    ["point"] = XyzToObject(point.Point)
                });
            }

            if (location is LocationCurve curve)
            {
                return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["kind"] = JsonValue.String("curve"),
                    ["length"] = JsonValue.Number(curve.Curve?.Length ?? 0),
                    ["start"] = XyzToObject(curve.Curve.GetEndPoint(0)),
                    ["end"] = XyzToObject(curve.Curve.GetEndPoint(1))
                });
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["kind"] = JsonValue.String("none")
            });
        }

        // --- Helpers ---

        private static string SafeString(Func<string> getter)
        {
            try { return getter() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string ParameterToString(Parameter p)
        {
            if (p == null || !p.HasValue)
            {
                return string.Empty;
            }

            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? string.Empty;
                case StorageType.Integer: return p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.Double: return p.AsDouble().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                {
                    ElementId id = p.AsElementId();
                    return id != null ? id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                }
                default: return string.Empty;
            }
        }

        private static JsonValue XyzToObject(XYZ xyz)
        {
            if (xyz == null)
            {
                return JsonValue.Null;
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["x"] = JsonValue.Number(xyz.X),
                ["y"] = JsonValue.Number(xyz.Y),
                ["z"] = JsonValue.Number(xyz.Z)
            });
        }
    }
}
