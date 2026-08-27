using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>roof.create</c>: creates a footprint roof from a closed outline on a level.
    ///
    /// Verified by compile probe #13: <c>Application</c> (and therefore
    /// <c>Application.Create.NewCurveArray()</c>) does NOT exist in this build — the container
    /// types <c>CurveArray</c> and <c>ModelCurveArray</c> are constructed directly with their
    /// parameterless constructors + <c>Append</c>. The factory is
    /// <c>Document.Create.NewFootPrintRoof(CurveArray, Level, RoofType, out ModelCurveArray)</c>
    /// (the mapping is an <c>out</c> parameter), and overhang is applied per footprint curve via
    /// <c>FootPrintRoof.set_Overhang(ModelCurve, double)</c>.
    /// </summary>
    public static class RoofCreateOperation
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
            var curveArray = new CurveArray();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                XYZ a = new XYZ(points[i]["x"].AsDouble() * scale, points[i]["y"].AsDouble() * scale, zFeet);
                XYZ b = new XYZ(points[j]["x"].AsDouble() * scale, points[j]["y"].AsDouble() * scale, zFeet);
                curveArray.Append(Line.CreateBound(a, b));
            }

            if (curveArray.Size < 3)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "roof.create outline needs at least 3 points.", true);
            }

            ElementId roofTypeId = FindRoofTypeId(document);
            var mapping = new ModelCurveArray();
            FootPrintRoof roof = document.Create.NewFootPrintRoof(curveArray, level, document.GetElement(roofTypeId) as RoofType, out mapping);
            if (roof == null)
            {
                throw new AgentException(ErrorCodes.ExecutionFailed, "roof.create produced no roof.", true);
            }

            var warnings = new List<string>();
            double overhang = args["overhang"].AsDouble(0);
            if (overhang > 0)
            {
                double overhangFeet = UnitNames.ToFeet(overhang, plan.Units);
                int applied = 0;
                if (mapping != null)
                {
                    foreach (ModelCurve mc in mapping)
                    {
                        try
                        {
                            roof.set_Overhang(mc, overhangFeet);
                            applied++;
                        }
                        catch
                        {
                            break;
                        }
                    }
                }

                if (applied == 0)
                {
                    warnings.Add("overhang could not be applied to the footprint curves (mapping empty); the roof was created without overhang.");
                }
            }

            string name = args["name"].AsString(null);
            if (!string.IsNullOrEmpty(name))
            {
                try { roof.Name = name; } catch { }
            }

            return new OperationResult(
                operation.Id,
                "roof.create",
                OperationOutcome.Completed,
                created: new[] { ElementResolver.Summarize(roof) },
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["level"] = JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["id"] = JsonValue.Number(level.Id.Value),
                        ["name"] = JsonValue.String(level.Name)
                    }),
                    ["type"] = JsonValue.Number(roofTypeId.Value),
                    ["overhang"] = JsonValue.Number(overhang),
                    ["corners"] = JsonValue.Number(n)
                }),
                warnings: warnings);
        }

        private static ElementId FindRoofTypeId(Document document)
        {
            foreach (RoofType rt in new FilteredElementCollector(document).OfClass(typeof(RoofType)))
            {
                return rt.Id;
            }

            throw new AgentException(ErrorCodes.TypeNotFound, "No RoofType available for roof.create.", true);
        }
    }
}
