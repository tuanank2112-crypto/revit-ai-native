using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>slab.create</c>: creates a floor slab from an outline polygon.
    /// The native Slab class is absent in this Revit build, so a Ceiling element created on
    /// the requested level is the supported floor equivalent (Ceiling is a CeilingAndFloor,
    /// i.e. a floor placed on the level it is given).
    ///
    /// Verified by compile probe #12/#13: <c>CurveLoop</c> has NO 1-argument constructor in
    /// this build (only the parameterless ctor + <c>Append</c>), and
    /// <c>Ceiling.Create(Document, IList&lt;CurveLoop&gt;, ElementId, ElementId)</c> is the
    /// supported factory. The slab sits on the requested level (e.g. the 1F level for the 2F
    /// floor slab).
    /// </summary>
    public static class SlabCreateOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            JsonValue outline = args["outline"];

            Level level = LevelResolver.Resolve(document, args["level"], plan.Units, document.ActiveView).Value;

            double scale = UnitNames.FeetPerUnit(plan.Units);
            double zFeet = level.Elevation;

            JsonValue points = outline["points"];
            int n = points.Count;
            var curves = new List<Curve>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                XYZ a = new XYZ(points[i]["x"].AsDouble() * scale, points[i]["y"].AsDouble() * scale, zFeet);
                XYZ b = new XYZ(points[j]["x"].AsDouble() * scale, points[j]["y"].AsDouble() * scale, zFeet);
                curves.Add(Line.CreateBound(a, b));
            }

            if (curves.Count < 3)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "slab.create outline needs at least 3 points.", true);
            }

            // CurveLoop: parameterless ctor + Append (1-arg ctor is absent in this build).
            var curveLoop = new CurveLoop();
            foreach (Curve c in curves)
            {
                curveLoop.Append(c);
            }

            var loops = new List<CurveLoop> { curveLoop };

            ElementId typeId = FindFloorTypeId(document);
            Ceiling slab = Ceiling.Create(document, loops, typeId, level.Id);
            if (slab == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "slab.create produced no slab.", true);
            }

            string name = args["name"].AsString(null);
            if (!string.IsNullOrEmpty(name))
            {
                try { slab.Name = name; } catch { }
            }

            return new OperationResult(
                operation.Id,
                "slab.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(slab) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name)
                    }),
                    ["type"] = JsonValue.Number(typeId.Value),
                    ["implementation"] = JsonValue.String("ceiling_as_floor"),
                    ["corners"] = JsonValue.Number(n)
                }));
        }

        private static ElementId FindFloorTypeId(Document document)
        {
            foreach (CeilingType ct in new FilteredElementCollector(document).OfClass(typeof(CeilingType)))
            {
                return ct.Id;
            }

            throw new AgentException(ErrorCodes.TypeNotFound, "No CeilingType available to use as a floor slab type.", true);
        }
    }
}
