using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>family.instance.create</c>: places a non-hosted family instance at a point.
    /// Used for columns, structural members and other generic elements that have no dedicated
    /// native creation API in this Revit build (Beam/Column classes are absent).
    ///
    /// NOTE (verified by compile probe #13): this Revit 2024 build exposes only the
    /// 3-arg <c>NewFamilyInstance(XYZ, FamilySymbol, StructuralType)</c> overload — there is
    /// no overload accepting a Level — and the <c>StructuralType</c> enum has exactly three
    /// values: <c>NonStructural</c>, <c>Column</c>, <c>Beam</c> (no "Structural" member).
    /// Rotation is not supported (no public rotation API in this build); a non-zero
    /// <c>rotation</c> arg is accepted for schema stability and surfaced as a warning.
    /// </summary>
    public static class FamilyInstanceCreateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results,
            ProjectPolicy policy)
        {
            JsonValue args = operation.Args;
            JsonValue point = args["point"];
            JsonValue typeSelector = args["type"];

            string categoryKey = args["category"].AsString("generic");
            StructuralType structuralType = MapStructuralType(args["structural"]);

            // The level is resolved for reporting/verification only; the creation overload in
            // this build does not take a level (placement is a pure XYZ in project coordinates).
            Level level = null;
            if (args.Has("level") && !args["level"].IsNull)
            {
                level = LevelResolver.Resolve(document, args["level"], plan.Units, document.ActiveView).Value;
            }

            FamilySymbol symbol = TypeResolver.Resolve(document, typeSelector, categoryKey, policy, plan.Units).Value;
            if (symbol == null)
            {
                throw new AgentException(ErrorCodes.TypeNotFound, "family.instance.create resolved no symbol.", true);
            }

            double scale = UnitNames.FeetPerUnit(plan.Units);
            XYZ placement = new XYZ(
                point["x"].AsDouble() * scale,
                point["y"].AsDouble() * scale,
                point["z"].AsDouble(0) * scale);

            if (!symbol.IsActive)
            {
                symbol.Activate();
                document.Regenerate();
            }

            FamilyInstance instance = document.Create.NewFamilyInstance(placement, symbol, structuralType);
            if (instance == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "family.instance.create produced no instance.", true);
            }

            // Optional name.
            string name = args["name"].AsString(null);
            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    instance.Name = name;
                }
                catch
                {
                    // Best-effort; some family instances do not allow renaming.
                }
            }

            var warnings = new List<string>();
            double rotationDeg = args["rotation"].AsDouble(0);
            if (Math.Abs(rotationDeg) > 1e-9)
            {
                warnings.Add("rotation is not supported in this Revit build; the instance was placed un-rotated.");
            }

            return new OperationResult(
                operation.Id,
                "family.instance.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(instance) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["type"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["name"] = JsonValue.String(symbol.Name),
                        ["family"] = JsonValue.String(symbol.FamilyName ?? string.Empty)
                    }),
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level != null ? level.Id.Value : 0),
                        ["name"] = JsonValue.String(level != null ? level.Name : string.Empty),
                        ["note"] = JsonValue.String("reporting only; creation overload takes no level in this build")
                    }),
                    ["structural"] = JsonValue.String(structuralType.ToString())
                }),
                warnings: warnings);
        }

        /// <summary>
        /// Maps the <c>structural</c> arg to one of the three StructuralType values present in
        /// this build. Default is Column (the common case: structural columns); "beam" yields
        /// Beam; "nonStructural"/"none"/"false" yields NonStructural.
        /// </summary>
        private static StructuralType MapStructuralType(JsonValue value)
        {
            if (value.Kind == JsonKind.Boolean)
            {
                return value.AsBool() ? StructuralType.Column : StructuralType.NonStructural;
            }

            string s = value.AsString("structural");
            switch (s)
            {
                case "beam": return StructuralType.Beam;
                case "nonStructural":
                case "none":
                case "false":
                case "false_structural":
                    return StructuralType.NonStructural;
                case "column":
                default:
                    return StructuralType.Column;
            }
        }
    }
}
