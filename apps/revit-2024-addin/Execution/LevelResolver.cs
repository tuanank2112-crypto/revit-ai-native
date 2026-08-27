using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>Result of a resolution with a human-readable trail.</summary>
    public sealed class Resolution<T>
    {
        /// <summary>Creates a resolution.</summary>
        public Resolution(T value, string strategy, string note = null)
        {
            Value = value;
            Strategy = strategy;
            Note = note;
        }

        /// <summary>Resolved value, or default(T) when failed.</summary>
        public T Value { get; }

        /// <summary>Which strategy succeeded (or was attempted).</summary>
        public string Strategy { get; }

        /// <summary>Human-readable note, e.g. "hard default used".</summary>
        public string Note { get; }
    }

    /// <summary>
    /// Resolves the level selector from wall.create/door.insert schemas. All strategies from
    /// the contract are implemented; failure is a structured AgentError, never a silent fallback.
    /// </summary>
    public static class LevelResolver
    {
        /// <summary>Resolves a level selector against a document.</summary>
        public static Resolution<Level> Resolve(Document document, JsonValue selector, ExternalUnit units, View activeView = null)
        {
            string strategy = selector["strategy"].AsString(null);
            switch (strategy)
            {
                case "explicit_unique_id":
                {
                    string uniqueId = selector["uniqueId"].AsString(null);
                    if (string.IsNullOrEmpty(uniqueId))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "level.uniqueId is required for explicit_unique_id.", true);
                    }

                    Element element = FindByUniqueId(document, uniqueId);
                    var level = element as Level;
                    if (level == null)
                    {
                        throw new AgentException(ErrorCodes.LevelNotFound, "No level found for uniqueId '" + uniqueId + "'.", true);
                    }

                    return new Resolution<Level>(level, "explicit_unique_id");
                }

                case "exact_name":
                {
                    string name = selector["name"].AsString(null);
                    if (string.IsNullOrEmpty(name))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "level.name is required for exact_name.", true);
                    }

                    Level match = null;
                    FilteredElementCollector collector = new FilteredElementCollector(document).OfClass(typeof(Level));
                    foreach (Level level in collector)
                    {
                        if (string.Equals(level.Name, name, StringComparison.Ordinal))
                        {
                            if (match != null)
                            {
                                throw new AgentException(ErrorCodes.AmbiguousLevel, "Multiple levels named '" + name + "'.", true);
                            }

                            match = level;
                        }
                    }

                    if (match == null)
                    {
                        throw new AgentException(ErrorCodes.LevelNotFound, "No level named '" + name + "'.", true);
                    }

                    return new Resolution<Level>(match, "exact_name");
                }

                case "active_view_level":
                {
                    Level level = activeView != null ? activeView.GenLevel : null;
                    if (level == null)
                    {
                        throw new AgentException(ErrorCodes.LevelNotFound, "The active view has no level.", true);
                    }

                    return new Resolution<Level>(level, "active_view_level");
                }

                case "nearest_by_elevation":
                {
                    double elevation = selector["elevation"].AsDouble(double.NaN);
                    if (double.IsNaN(elevation))
                    {
                        throw new AgentException(ErrorCodes.MissingArgument, "level.elevation is required for nearest_by_elevation.", true);
                    }

                    double targetFeet = UnitNames.ToFeet(elevation, units);
                    Level best = null;
                    double bestDistance = double.MaxValue;
                    FilteredElementCollector collector = new FilteredElementCollector(document).OfClass(typeof(Level));
                    foreach (Level level in collector)
                    {
                        double distance = Math.Abs(level.Elevation - targetFeet);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = level;
                        }
                    }

                    if (best == null)
                    {
                        throw new AgentException(ErrorCodes.LevelNotFound, "No levels exist in the document.", true);
                    }

                    return new Resolution<Level>(best, "nearest_by_elevation", "distance " + bestDistance.ToString("0.0000"));
                }

                case "project_default_or_fail":
                {
                    Level best = null;
                    FilteredElementCollector collector = new FilteredElementCollector(document).OfClass(typeof(Level));
                    foreach (Level level in collector)
                    {
                        if (best == null || level.Elevation < best.Elevation)
                        {
                            best = level;
                        }
                    }

                    if (best == null)
                    {
                        throw new AgentException(ErrorCodes.LevelNotFound, "No level to default to.", true);
                    }

                    return new Resolution<Level>(best, "project_default_or_fail", "lowest level");
                }

                case "host_level":
                    // Resolved by the operation that has the host; callers use ResolveFromHost.
                    throw new AgentException(ErrorCodes.InvalidArgument, "host_level must be resolved from the host element.", true);

                default:
                    throw new AgentException(ErrorCodes.InvalidArgument, "Unsupported level strategy '" + (strategy ?? "<missing>") + "'.", true);
            }
        }

        /// <summary>Finds the level hosting a given element (for host_level).</summary>
        public static Resolution<Level> FromHost(Document document, Element host)
        {
            if (host == null)
            {
                throw new AgentException(ErrorCodes.InvalidHost, "Cannot resolve host_level: host is null.", true);
            }

            Level level = document.GetElement(host.LevelId) as Level;
            if (level == null)
            {
                throw new AgentException(ErrorCodes.LevelNotFound, "The host element has no level.", true);
            }

            return new Resolution<Level>(level, "host_level");
        }

        internal static Element FindByUniqueId(Document document, string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
            {
                return null;
            }

            try
            {
                return document.GetElement(uniqueId);
            }
            catch
            {
                return null;
            }
        }
    }
}
