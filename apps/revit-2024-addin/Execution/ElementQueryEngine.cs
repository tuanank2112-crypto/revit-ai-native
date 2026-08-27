using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Safe element query engine. Accepts the structured query format from
    /// query.schema.json and translates it into FilteredElementCollector filters.
    /// No raw expressions, no LINQ strings, no reflection.
    /// </summary>
    public static class ElementQueryEngine
    {
        private const int DefaultLimit = 100;
        private const int MaxLimit = 1000;

        /// <summary>Executes a query against the document.</summary>
        public static JsonValue Execute(Document document, JsonValue query)
        {
            if (document == null)
            {
                throw new AgentException(ErrorCodes.NoActiveDocument, "No active document.", true);
            }

            if (query == null || !query.IsObject)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "Query must be a JSON object.", true);
            }

            JsonValue categories = query["categories"];
            if (!categories.IsArray || categories.Count == 0)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "Query must specify at least one category.", true);
            }

            int limit = query["limit"].AsInt(DefaultLimit);
            if (limit < 1) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;

            // Build the collector for the given categories.
            var builtInCategories = new List<BuiltInCategory>();
            foreach (JsonValue cat in categories.Items)
            {
                string catName = cat.AsString(null);
                BuiltInCategory bic = ResolveCategoryName(catName);
                if (!builtInCategories.Contains(bic))
                {
                    builtInCategories.Add(bic);
                }
            }

            var multiCatFilter = new ElementCategoryFilter(builtInCategories[0]);
            for (int i = 1; i < builtInCategories.Count; i++)
            {
                multiCatFilter = new ElementCategoryFilter(builtInCategories[i]) as ElementCategoryFilter;
            }

            var collector = new FilteredElementCollector(document)
                .WherePasses(new LogicalOrFilter(
                    builtInCategories.ConvertAll(c => (ElementFilter)new ElementCategoryFilter(c))));

            collector = collector.WhereElementIsNotElementType();

            // Apply the "where" clause if present.
            JsonValue where = query["where"];
            if (where.IsObject)
            {
                var matchingIds = new List<ElementId>();
                foreach (Element element in collector)
                {
                    if (MatchesWhere(element, where))
                    {
                        matchingIds.Add(element.Id);
                        if (matchingIds.Count >= limit)
                        {
                            break;
                        }
                    }
                }

                return BuildResults(document, matchingIds, query);
            }

            // No where clause: just take the first N.
            var limitedIds = new List<ElementId>();
            foreach (Element element in collector)
            {
                limitedIds.Add(element.Id);
                if (limitedIds.Count >= limit)
                {
                    break;
                }
            }

            return BuildResults(document, limitedIds, query);
        }

        private static JsonValue BuildResults(Document document, List<ElementId> ids, JsonValue query)
        {
            var items = new List<JsonValue>();
            foreach (ElementId id in ids)
            {
                Element element = document.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                var summary = ElementResolver.Summarize(element);
                var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["elementId"] = JsonValue.Number(summary.ElementId),
                    ["uniqueId"] = JsonValue.String(summary.UniqueId),
                    ["category"] = JsonValue.String(summary.Category),
                    ["name"] = JsonValue.String(summary.Name ?? string.Empty),
                    ["typeName"] = JsonValue.String(summary.TypeName ?? string.Empty)
                };

                // Include location if available.
                if (element.Location is LocationPoint point)
                {
                    members["location"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["x"] = JsonValue.Number(point.Point.X),
                        ["y"] = JsonValue.Number(point.Point.Y),
                        ["z"] = JsonValue.Number(point.Point.Z)
                    });
                }
                else if (element.Location is LocationCurve curve)
                {
                    members["location"] = JsonValue.String("curve");
                }

                items.Add(JsonValue.Object(members));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["elements"] = JsonValue.Array(items),
                ["count"] = JsonValue.Number(items.Count)
            });
        }

        private static bool MatchesWhere(Element element, JsonValue where)
        {
            // "all" = AND, "any" = OR.
            JsonValue all = where["all"];
            if (all.IsArray)
            {
                foreach (JsonValue condition in all.Items)
                {
                    if (!MatchesCondition(element, condition))
                    {
                        return false;
                    }
                }
            }

            JsonValue any = where["any"];
            if (any.IsArray)
            {
                bool anyMatched = false;
                foreach (JsonValue condition in any.Items)
                {
                    if (MatchesCondition(element, condition))
                    {
                        anyMatched = true;
                        break;
                    }
                }

                if (!anyMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesCondition(Element element, JsonValue condition)
        {
            // A condition can be a direct {field, operator, value} or nested {all: [...]} / {any: [...]}.
            if (condition.Has("all"))
            {
                foreach (JsonValue nested in condition["all"].Items)
                {
                    if (!MatchesCondition(element, nested))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (condition.Has("any"))
            {
                foreach (JsonValue nested in condition["any"].Items)
                {
                    if (MatchesCondition(element, nested))
                    {
                        return true;
                    }
                }

                return false;
            }

            string field = condition["field"].AsString(null);
            string op = condition["operator"].AsString(null);
            JsonValue value = condition["value"];

            if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(op))
            {
                return false;
            }

            string fieldValue = ResolveFieldValue(element, field);

            switch (op)
            {
                case "equals":
                    return string.Equals(fieldValue, value.AsString(), StringComparison.OrdinalIgnoreCase);

                case "not_equals":
                    return !string.Equals(fieldValue, value.AsString(), StringComparison.OrdinalIgnoreCase);

                case "contains":
                    return (fieldValue ?? string.Empty).IndexOf(value.AsString(string.Empty), StringComparison.OrdinalIgnoreCase) >= 0;

                case "starts_with":
                    return (fieldValue ?? string.Empty).StartsWith(value.AsString(string.Empty), StringComparison.OrdinalIgnoreCase);

                case "ends_with":
                    return (fieldValue ?? string.Empty).EndsWith(value.AsString(string.Empty), StringComparison.OrdinalIgnoreCase);

                case "greater_than":
                {
                    double d;
                    return double.TryParse(fieldValue, out d) && d > value.AsDouble(double.NaN);
                }

                case "greater_or_equal":
                {
                    double d;
                    return double.TryParse(fieldValue, out d) && d >= value.AsDouble(double.NaN);
                }

                case "less_than":
                {
                    double d;
                    return double.TryParse(fieldValue, out d) && d < value.AsDouble(double.NaN);
                }

                case "less_or_equal":
                {
                    double d;
                    return double.TryParse(fieldValue, out d) && d <= value.AsDouble(double.NaN);
                }

                case "is_empty":
                    return string.IsNullOrEmpty(fieldValue);

                case "is_not_empty":
                    return !string.IsNullOrEmpty(fieldValue);

                case "in":
                {
                    if (!value.IsArray)
                    {
                        return false;
                    }

                    foreach (JsonValue candidate in value.Items)
                    {
                        if (string.Equals(fieldValue, candidate.AsString(), StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                default:
                    return false;
            }
        }

        private static string ResolveFieldValue(Element element, string field)
        {
            if (element == null || string.IsNullOrEmpty(field))
            {
                return null;
            }

            // Structured field paths.
            if (string.Equals(field, "name", StringComparison.OrdinalIgnoreCase))
            {
                return element.Name;
            }

            if (string.Equals(field, "category", StringComparison.OrdinalIgnoreCase))
            {
                return element.Category?.Name;
            }

            if (string.Equals(field, "type.name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(field, "type_name", StringComparison.OrdinalIgnoreCase))
            {
                ElementType type = element.Document.GetElement(element.GetTypeId()) as ElementType;
                return type?.Name;
            }

            if (string.Equals(field, "level.name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(field, "level_name", StringComparison.OrdinalIgnoreCase))
            {
                Level level = element.Document.GetElement(element.LevelId) as Level;
                return level?.Name;
            }

            if (field.StartsWith("parameter.", StringComparison.OrdinalIgnoreCase))
            {
                string paramName = field.Substring("parameter.".Length);
                Parameter p = element.LookupParameter(paramName);
                return ParameterToString(p);
            }

            // Fallback: try as a parameter name.
            Parameter param = element.LookupParameter(field);
            return ParameterToString(param);
        }

        private static string ParameterToString(Parameter p)
        {
            if (p == null || !p.HasValue)
            {
                return null;
            }

            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.Double: return p.AsDouble().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                {
                    ElementId id = p.AsElementId();
                    return id != null ? id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                }
                default: return null;
            }
        }

        private static BuiltInCategory ResolveCategoryName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return BuiltInCategory.OST_GenericModel;
            }

            switch (name.ToLowerInvariant())
            {
                case "walls":
                case "wall": return BuiltInCategory.OST_Walls;
                case "doors":
                case "door": return BuiltInCategory.OST_Doors;
                case "windows":
                case "window": return BuiltInCategory.OST_Windows;
                case "floors":
                case "floor": return BuiltInCategory.OST_Floors;
                case "roofs":
                case "roof": return BuiltInCategory.OST_Roofs;
                case "ceilings":
                case "ceiling": return BuiltInCategory.OST_Ceilings;
                case "columns":
                case "column": return BuiltInCategory.OST_StructuralColumns;
                case "furniture": return BuiltInCategory.OST_Furniture;
                case "rooms":
                case "room": return BuiltInCategory.OST_Rooms;
                case "sections":
                case "section": return BuiltInCategory.OST_Sections;
                case "elevations":
                case "elevation": return BuiltInCategory.OST_Elev;
                case "grid":
                case "grids": return BuiltInCategory.OST_Grids;
                case "levels":
                case "level": return BuiltInCategory.OST_Levels;
                default: return BuiltInCategory.OST_GenericModel;
            }
        }
    }
}
