using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Revit2024.Execution;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Resolves the type selector from wall.create/door.insert schemas. Type resolution can
    /// fall back through the declared strategies; a failure always names the strategy that
    /// was attempted so the agent can correct the plan.
    /// </summary>
    public static class TypeResolver
    {
        /// <summary>Resolves a family symbol for the given category, using the plan's type selector.</summary>
        public static Resolution<FamilySymbol> Resolve(Document document, JsonValue selector, string categoryKey, ProjectPolicy policy, ExternalUnit units)
        {
            string strategy = selector["strategy"].AsString(null);
            switch (strategy)
            {
                case "explicit_unique_id":
                {
                    string uniqueId = selector["uniqueId"].AsString(null);
                    if (string.IsNullOrEmpty(uniqueId))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "type.uniqueId is required for explicit_unique_id.", true);
                    }

                    Element element = LevelResolver.FindByUniqueId(document, uniqueId);
                    var symbol = element as FamilySymbol;
                    if (symbol == null)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound, "No family symbol found for uniqueId '" + uniqueId + "'.", true);
                    }

                    return new Resolution<FamilySymbol>(symbol, "explicit_unique_id");
                }

                case "exact_name":
                {
                    string familyName = selector["familyName"].AsString(null);
                    string typeName = selector["typeName"].AsString(null);
                    if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(familyName))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "type.name requires familyName or typeName.", true);
                    }

                    FamilySymbol match = null;
                    int count = 0;
                    foreach (FamilySymbol symbol in CollectSymbols(document, categoryKey))
                    {
                        if (MatchesName(symbol, familyName, typeName))
                        {
                            match = symbol;
                            count++;
                        }
                    }

                    if (count > 1)
                    {
                        throw new AgentException(ErrorCodes.AmbiguousType,
                            "Multiple types match familyName/typeName '" + familyName + "/" + typeName + "'.", true);
                    }

                    if (match == null)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound,
                            "No " + categoryKey + " type matches '" + familyName + "/" + typeName + "'.", true);
                    }

                    return new Resolution<FamilySymbol>(match, "exact_name");
                }

                case "project_policy":
                {
                    IReadOnlyList<string> preferred = policy.GetPreferredTypes(categoryKey);
                    if (preferred.Count == 0)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound,
                            "Project policy defines no preferred " + categoryKey + " types.", true);
                    }

                    foreach (string name in preferred)
                    {
                        foreach (FamilySymbol symbol in CollectSymbols(document, categoryKey))
                        {
                            if (symbol.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return new Resolution<FamilySymbol>(symbol, "project_policy", "policy name '" + name + "'");
                            }
                        }
                    }

                    throw new AgentException(ErrorCodes.TypeNotFound,
                        "No " + categoryKey + " type matches the project policy names.", true);
                }

                case "project_default_or_fail":
                {
                    // First symbol in document order; used when the agent explicitly says "just pick one".
                    FamilySymbol first = null;
                    foreach (FamilySymbol symbol in CollectSymbols(document, categoryKey))
                    {
                        first = symbol;
                        break;
                    }

                    if (first == null)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound,
                            "No " + categoryKey + " types exist in the document.", true);
                    }

                    return new Resolution<FamilySymbol>(first, "project_default_or_fail");
                }

                case "preferred_or_dimensions":
                {
                    double desiredWidth = selector["desiredWidth"].AsDouble(double.NaN);
                    double desiredHeight = selector["desiredHeight"].AsDouble(double.NaN);

                    // 1. Try the policy's preferred names.
                    foreach (string name in policy.GetPreferredTypes(categoryKey))
                    {
                        foreach (FamilySymbol symbol in CollectSymbols(document, categoryKey))
                        {
                            if (symbol.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return new Resolution<FamilySymbol>(symbol, "preferred_or_dimensions", "policy name");
                            }
                        }
                    }

                    // 2. Try to match by dimensions from the symbol's type parameters.
                    FamilySymbol best = null;
                    double bestError = double.MaxValue;
                    foreach (FamilySymbol symbol in CollectSymbols(document, categoryKey))
                    {
                        double error = DimensionError(symbol, desiredWidth, desiredHeight, units);
                        if (error < bestError)
                        {
                            bestError = error;
                            best = symbol;
                        }
                    }

                    if (best != null && !double.IsNaN(desiredWidth) && !double.IsNaN(desiredHeight))
                    {
                        return new Resolution<FamilySymbol>(best, "preferred_or_dimensions",
                            "dimension match error " + bestError.ToString("0.00"));
                    }

                    if (best != null && double.IsNaN(desiredWidth) && double.IsNaN(desiredHeight))
                    {
                        return new Resolution<FamilySymbol>(best, "preferred_or_dimensions", "first available");
                    }

                    throw new AgentException(ErrorCodes.TypeNotFound,
                        "No " + categoryKey + " type could be matched by dimensions.", true);
                }

                case "most_used_in_project":
                {
                    FamilySymbol mostUsed = null;
                    int mostCount = 0;
                    foreach (FamilySymbol symbol in CollectSymbols(document, categoryKey))
                    {
                        int count = CountInstances(document, symbol);
                        if (count > mostCount)
                        {
                            mostCount = count;
                            mostUsed = symbol;
                        }
                    }

                    if (mostUsed == null)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound,
                            "No " + categoryKey + " types exist in the document.", true);
                    }

                    return new Resolution<FamilySymbol>(mostUsed, "most_used_in_project", mostCount + " instances");
                }

                default:
                    throw new AgentException(ErrorCodes.InvalidArgument,
                        "Unsupported type strategy '" + (strategy ?? "<missing>") + "'.", true);
            }
        }

        /// <summary>Resolves a WallType from a type selector for wall.update.</summary>
        public static WallType ResolveWallType(Document document, JsonValue selector)
        {
            string strategy = selector["strategy"].AsString(null);

            if (strategy == "explicit_unique_id")
            {
                string uniqueId = selector["uniqueId"].AsString(null);
                if (string.IsNullOrEmpty(uniqueId))
                {
                    throw new AgentException(ErrorCodes.MissingArgument, "type.uniqueId is required for explicit_unique_id.", true);
                }

                Element element = LevelResolver.FindByUniqueId(document, uniqueId);
                var wallType = element as WallType;
                if (wallType == null)
                {
                    throw new AgentException(ErrorCodes.TypeNotFound, "No wall type found for uniqueId '" + uniqueId + "'.", true);
                }

                return wallType;
            }

            if (strategy == "exact_name")
            {
                string typeName = selector["typeName"].AsString(null);
                if (string.IsNullOrEmpty(typeName))
                {
                    throw new AgentException(ErrorCodes.MissingArgument, "type.typeName is required for exact_name.", true);
                }

                var collector = new FilteredElementCollector(document).OfClass(typeof(WallType));
                foreach (WallType wt in collector)
                {
                    if (string.Equals(wt.Name, typeName, StringComparison.Ordinal))
                    {
                        return wt;
                    }
                }

                throw new AgentException(ErrorCodes.TypeNotFound, "No wall type named '" + typeName + "'.", true);
            }

            throw new AgentException(ErrorCodes.InvalidArgument,
                "Unsupported type strategy '" + (strategy ?? "<missing>") + "' for wall type resolution.", true);
        }

        /// <summary>
        /// Resolves a WallType from the wall.create type selector, supporting all
        /// strategies in wall.create.schema.json. System walls are WallType elements
        /// (not FamilySymbols), so this path collects &lt;see cref="WallType"/&gt; directly.
        /// </summary>
        public static WallType ResolveWallTypeBySelector(Document document, JsonValue selector, ExternalUnit units)
        {
            string strategy = selector["strategy"].AsString(null);
            switch (strategy)
            {
                case "explicit_unique_id":
                {
                    string uniqueId = selector["uniqueId"].AsString(null);
                    if (string.IsNullOrEmpty(uniqueId))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "type.uniqueId is required for explicit_unique_id.", true);
                    }

                    Element element = LevelResolver.FindByUniqueId(document, uniqueId);
                    var wallType = element as WallType;
                    if (wallType == null)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound, "No wall type found for uniqueId '" + uniqueId + "'.", true);
                    }

                    return wallType;
                }

                case "exact_name":
                {
                    string typeName = selector["typeName"].AsString(null);
                    if (string.IsNullOrEmpty(typeName))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "type.typeName is required for exact_name.", true);
                    }

                    foreach (WallType wt in CollectWallTypes(document))
                    {
                        if (string.Equals(wt.Name, typeName, StringComparison.Ordinal))
                        {
                            return wt;
                        }
                    }

                    throw new AgentException(ErrorCodes.TypeNotFound, "No wall type named '" + typeName + "'.", true);
                }

                case "project_policy":
                {
                    foreach (WallType wt in CollectWallTypes(document))
                    {
                        return wt;
                    }

                    throw new AgentException(ErrorCodes.TypeNotFound, "No wall types exist in the document.", true);
                }

                case "project_default_or_fail":
                {
                    foreach (WallType wt in CollectWallTypes(document))
                    {
                        return wt;
                    }

                    throw new AgentException(ErrorCodes.TypeNotFound, "No wall types exist in the document.", true);
                }

                case "preferred_or_dimensions":
                {
                    double desiredWidth = selector["desiredWidth"].AsDouble(double.NaN);
                    double desiredHeight = selector["desiredHeight"].AsDouble(double.NaN);

                    WallType best = null;
                    double bestError = double.MaxValue;
                    int count = 0;
                    foreach (WallType wt in CollectWallTypes(document))
                    {
                        count++;
                        double error = WallTypeDimensionError(wt, desiredWidth, desiredHeight, units);
                        if (error < bestError)
                        {
                            bestError = error;
                            best = wt;
                        }
                    }

                    if (count == 0)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound, "No wall types exist in the document.", true);
                    }

                    return best;
                }

                case "most_used_in_project":
                {
                    WallType mostUsed = null;
                    int mostCount = 0;
                    foreach (WallType wt in CollectWallTypes(document))
                    {
                        int count = CountWallInstances(document, wt);
                        if (count > mostCount)
                        {
                            mostCount = count;
                            mostUsed = wt;
                        }
                    }

                    if (mostUsed == null)
                    {
                        throw new AgentException(ErrorCodes.TypeNotFound, "No wall types exist in the document.", true);
                    }

                    return mostUsed;
                }

                default:
                    throw new AgentException(ErrorCodes.InvalidArgument,
                        "Unsupported type strategy '" + (strategy ?? "<missing>") + "' for wall type resolution.", true);
            }
        }

        private static IEnumerable<WallType> CollectWallTypes(Document document)
        {
            foreach (WallType wt in new FilteredElementCollector(document).OfClass(typeof(WallType)))
            {
                yield return wt;
            }
        }

        private static int CountWallInstances(Document document, WallType wallType)
        {
            FilteredElementCollector collector = new FilteredElementCollector(document)
                .OfClass(typeof(Wall))
                .WherePasses(new ElementClassFilter(typeof(Wall)));
            int count = 0;
            foreach (Wall wall in collector)
            {
                if (wall.WallType != null && wall.WallType.Id == wallType.Id)
                {
                    count++;
                }
            }

            return count;
        }

        private static double WallTypeDimensionError(WallType wallType, double desiredWidth, double desiredHeight, ExternalUnit units)
        {
            double error = 0;
            double w = GetTypeParamFeet(wallType, "Width");
            double h = GetTypeParamFeet(wallType, "Height");
            if (!double.IsNaN(w) && !double.IsNaN(desiredWidth))
            {
                error += Math.Abs(UnitNames.FromFeet(w, units) - desiredWidth);
            }

            if (!double.IsNaN(h) && !double.IsNaN(desiredHeight))
            {
                error += Math.Abs(UnitNames.FromFeet(h, units) - desiredHeight);
            }

            return error;
        }

        private static double GetTypeParamFeet(ElementType elementType, string name)
        {
            Parameter parameter = elementType.LookupParameter(name);
            if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
            {
                return parameter.AsDouble();
            }

            return double.NaN;
        }

        private static IEnumerable<FamilySymbol> CollectSymbols(Document document, string categoryKey)
        {
            BuiltInCategory bic = CategoryKeyToBuiltIn(categoryKey);
            FilteredElementCollector collector = new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol));
            foreach (FamilySymbol symbol in collector)
            {
                if (symbol.Category != null && symbol.Category.Id.Value == (int)bic)
                {
                    yield return symbol;
                }
            }
        }

        private static int CountInstances(Document document, FamilySymbol symbol)
        {
            FilteredElementCollector collector = new FilteredElementCollector(document)
                .OfClass(typeof(FamilyInstance))
                .WherePasses(new FamilyInstanceFilter(document, symbol.Id));
            return collector.GetElementCount();
        }

        private static double DimensionError(FamilySymbol symbol, double desiredWidth, double desiredHeight, ExternalUnit units)
        {
            double error = 0;
            double w = GetTypeParamFeet(symbol, "Width");
            double h = GetTypeParamFeet(symbol, "Height");
            if (!double.IsNaN(w) && !double.IsNaN(desiredWidth))
            {
                error += Math.Abs(UnitNames.FromFeet(w, units) - desiredWidth);
            }

            if (!double.IsNaN(h) && !double.IsNaN(desiredHeight))
            {
                error += Math.Abs(UnitNames.FromFeet(h, units) - desiredHeight);
            }

            return error;
        }

        private static double GetTypeParamFeet(FamilySymbol symbol, string name)
        {
            Parameter parameter = symbol.LookupParameter(name);
            if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
            {
                return parameter.AsDouble();
            }

            return double.NaN;
        }

        private static bool MatchesName(FamilySymbol symbol, string familyName, string typeName)
        {
            if (!string.IsNullOrEmpty(familyName) && !string.IsNullOrEmpty(symbol.FamilyName) &&
                !string.Equals(symbol.FamilyName, familyName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(typeName) && !string.Equals(symbol.Name, typeName, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static BuiltInCategory CategoryKeyToBuiltIn(string key)
        {
            switch (key)
            {
                case "wall": return BuiltInCategory.OST_Walls;
                case "door": return BuiltInCategory.OST_Doors;
                case "window": return BuiltInCategory.OST_Windows;
                default: return BuiltInCategory.OST_GenericModel;
            }
        }
    }
}
