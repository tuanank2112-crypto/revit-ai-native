using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Policy
{
    /// <summary>
    /// Parsed form of config/project-policy.json (schema: project-policy.schema.json).
    /// Supplies project-specific defaults, preferred types, tolerances and safety rules.
    /// The runtime never reads this file itself; hosts parse it and pass the object in.
    /// </summary>
    public sealed class ProjectPolicy
    {
        /// <summary>The only accepted policy version.</summary>
        public const string CurrentPolicyVersion = "1.0";

        /// <summary>Creates a policy with conservative defaults.</summary>
        public ProjectPolicy()
        {
            DefaultExternalUnit = ExternalUnit.Mm;
            AllowHardDefaults = false;
            Defaults = new Dictionary<string, double>(StringComparer.Ordinal);
            PreferredTypes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            LengthToleranceMm = 1d;
            PositionToleranceMm = 1d;
            AngleToleranceDegrees = 0.1d;
            AlwaysPreviewDelete = true;
            AlwaysPreviewMoreThanElements = 20;
            RequireConfirmationForSaveAs = true;
            RequireConfirmationForExport = false;
            ExportRootDirectories = new List<string>();
        }

        /// <summary>Declared policy version.</summary>
        public string PolicyVersion { get; private set; }

        /// <summary>Unit the policy's numeric defaults are expressed in.</summary>
        public ExternalUnit DefaultExternalUnit { get; private set; }

        /// <summary>True when hard defaults may be silently substituted.</summary>
        public bool AllowHardDefaults { get; private set; }

        /// <summary>Policy-level default values, keyed by schema name (e.g. wallHeightMm).</summary>
        public IDictionary<string, double> Defaults { get; }

        /// <summary>Preferred family/type names keyed by category (wall, door, window, titleBlock).</summary>
        public IDictionary<string, List<string>> PreferredTypes { get; }

        /// <summary>Default length tolerance, in millimetres.</summary>
        public double LengthToleranceMm { get; private set; }

        /// <summary>Default position tolerance, in millimetres.</summary>
        public double PositionToleranceMm { get; private set; }

        /// <summary>Default angle tolerance, in degrees.</summary>
        public double AngleToleranceDegrees { get; private set; }

        /// <summary>Always require a preview (and confirmation) before any delete.</summary>
        public bool AlwaysPreviewDelete { get; private set; }

        /// <summary>Always preview when the estimated affected count exceeds this.</summary>
        public int AlwaysPreviewMoreThanElements { get; private set; }

        /// <summary>Require confirmation before SaveAs.</summary>
        public bool RequireConfirmationForSaveAs { get; private set; }

        /// <summary>Require confirmation before export.</summary>
        public bool RequireConfirmationForExport { get; private set; }

        /// <summary>Canonicalized roots under which export paths are allowed.</summary>
        public IReadOnlyList<string> ExportRootDirectories { get; private set; }

        /// <summary>Parses a policy from its wire form. Never throws; malformed input produces conservative defaults.</summary>
        public static ProjectPolicy FromJson(JsonValue json)
        {
            var policy = new ProjectPolicy();

            if (json == null || !json.IsObject)
            {
                return policy;
            }

            policy.PolicyVersion = json["policyVersion"].AsString(CurrentPolicyVersion);

            string unitText = json["defaultExternalUnit"].AsString(null);
            ExternalUnit unit;
            if (UnitNames.TryParseLength(unitText, out unit))
            {
                policy.DefaultExternalUnit = unit;
            }

            policy.AllowHardDefaults = json["allowHardDefaults"].AsBool(false);

            JsonValue defaults = json["defaults"];
            if (defaults.IsObject)
            {
                foreach (var member in defaults.Members)
                {
                    double value;
                    if (member.Value.TryGetDouble(out value))
                    {
                        policy.Defaults[member.Key] = value;
                    }
                }
            }

            JsonValue preferred = json["preferredTypes"];
            if (preferred.IsObject)
            {
                foreach (var member in preferred.Members)
                {
                    var names = new List<string>();
                    if (member.Value.IsArray)
                    {
                        foreach (JsonValue item in member.Value.Items)
                        {
                            string name = item.AsString(null);
                            if (!string.IsNullOrEmpty(name))
                            {
                                names.Add(name);
                            }
                        }
                    }

                    if (names.Count > 0)
                    {
                        policy.PreferredTypes[member.Key] = names;
                    }
                }
            }

            JsonValue tolerances = json["tolerances"];
            if (tolerances.IsObject)
            {
                policy.LengthToleranceMm = tolerances["lengthMm"].AsDouble(policy.LengthToleranceMm);
                policy.PositionToleranceMm = tolerances["positionMm"].AsDouble(policy.PositionToleranceMm);
                policy.AngleToleranceDegrees = tolerances["angleDegrees"].AsDouble(policy.AngleToleranceDegrees);
            }

            JsonValue safety = json["safety"];
            if (safety.IsObject)
            {
                policy.AlwaysPreviewDelete = safety["alwaysPreviewDelete"].AsBool(policy.AlwaysPreviewDelete);
                policy.AlwaysPreviewMoreThanElements = safety["alwaysPreviewMoreThanElements"].AsInt(policy.AlwaysPreviewMoreThanElements);
                policy.RequireConfirmationForSaveAs = safety["requireConfirmationForSaveAs"].AsBool(policy.RequireConfirmationForSaveAs);
                policy.RequireConfirmationForExport = safety["requireConfirmationForExport"].AsBool(policy.RequireConfirmationForExport);

                JsonValue roots = safety["exportRootDirectories"];
                if (roots.IsArray)
                {
                    var list = new List<string>();
                    foreach (JsonValue item in roots.Items)
                    {
                        string root = item.AsString(null);
                        if (!string.IsNullOrEmpty(root))
                        {
                            list.Add(root);
                        }
                    }

                    if (list.Count > 0)
                    {
                        policy.ExportRootDirectories = list;
                    }
                }
            }

            return policy;
        }

        /// <summary>Finds the default value by schema key (e.g. wallHeightMm), or null.</summary>
        public double? FindDefault(string key)
        {
            double value;
            if (Defaults.TryGetValue(key, out value))
            {
                return value;
            }

            return null;
        }

        /// <summary>Gets the preferred type names for a category key, or an empty list.</summary>
        public IReadOnlyList<string> GetPreferredTypes(string key)
        {
            List<string> names;
            if (PreferredTypes.TryGetValue(key, out names))
            {
                return names;
            }

            return System.Array.Empty<string>();
        }
    }
}
