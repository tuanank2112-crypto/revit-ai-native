using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>
    /// Executes <c>parameter.set_many</c>: sets multiple typed parameters on one or more
    /// elements atomically. All items are validated before any write occurs.
    /// </summary>
    public static class ParameterSetManyOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            JsonValue items = args["items"];

            if (!items.IsArray || items.Count == 0)
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "items must be a non-empty array.", true);
            }

            // Phase 1: Validate all items without writing.
            var validated = new List<ValidatedItem>(items.Count);
            var perItemStatus = new List<JsonValue>();

            for (int i = 0; i < items.Count; i++)
            {
                JsonValue item = items.Items[i];
                int index = i;

                try
                {
                    Element target = ElementResolver.Resolve(document, item["target"], results);
                    Parameter param = ResolveParameter(target, item);
                    object value = ConvertValue(param, item["value"], item["unit"].AsString(null), plan.Units);
                    validated.Add(new ValidatedItem { Target = target, Parameter = param, Value = value, Index = index });
                    perItemStatus.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["index"] = JsonValue.Number(index),
                        ["valid"] = JsonValue.Bool(true)
                    }));
                }
                catch (AgentException ex)
                {
                    perItemStatus.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["index"] = JsonValue.Number(index),
                        ["valid"] = JsonValue.Bool(false),
                        ["error"] = new AgentError(ex.Code, ex.Message, ex.Recoverable).ToJson()
                    }));

                    // Atomic mode: any invalid item aborts the entire operation.
                    throw new AgentException(ex.Code,
                        "Item " + index + " invalid: " + ex.Message, ex.Recoverable, ex.SuggestedAction);
                }
            }

            // Phase 2: Apply all validated items.
            var modified = new List<ElementSummary>();
            foreach (ValidatedItem item in validated)
            {
                ApplyValue(item.Parameter, item.Value);
                if (!modified.Contains(ElementResolver.Summarize(item.Target)))
                {
                    modified.Add(ElementResolver.Summarize(item.Target));
                }
            }

            // Phase 3: Read back and verify.
            var measurements = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < validated.Count; i++)
            {
                string readBack = ReadParameterValue(validated[i].Parameter);
                measurements["item_" + i] = JsonValue.String(readBack ?? string.Empty);
            }

            return new OperationResult(
                operation.Id,
                "parameter.set_many",
                OperationOutcome.Completed,
                modified: modified,
                resolved: JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["items"] = JsonValue.Array(perItemStatus),
                    ["count"] = JsonValue.Number(validated.Count)
                }),
                measurements: measurements);
        }

        private static Parameter ResolveParameter(Element element, JsonValue item)
        {
            string name = item["parameter"].AsString(null);
            string guid = item["guid"].AsString(null);

            if (!string.IsNullOrEmpty(guid))
            {
                Parameter p = element.get_Parameter(new Guid(guid));
                if (p == null || !p.HasValue)
                {
                    throw new AgentException(ErrorCodes.ParameterNotFound,
                        "No shared parameter with GUID '" + guid + "'.", true);
                }

                return p;
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new AgentException(ErrorCodes.InvalidArgument, "parameter name or guid is required.", true);
            }

            // Try BuiltInParameter first.
            BuiltInParameter bip;
            if (Enum.TryParse(name, true, out bip))
            {
                Parameter p = element.get_Parameter(bip);
                if (p != null && p.HasValue)
                {
                    return p;
                }
            }

            // Fall back to exact name. Count matches to detect ambiguity.
            var matches = new List<Parameter>();
            foreach (Parameter p in element.Parameters)
            {
                if (p.Definition != null &&
                    string.Equals(p.Definition.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(p);
                }
            }

            if (matches.Count == 0)
            {
                throw new AgentException(ErrorCodes.ParameterNotFound,
                    "No parameter named '" + name + "' on element " + element.Id + ".", true);
            }

            if (matches.Count > 1)
            {
                throw new AgentException(ErrorCodes.AmbiguousParameter,
                    "Multiple parameters named '" + name + "' on element " + element.Id + ".", true);
            }

            if (matches[0].IsReadOnly)
            {
                throw new AgentException(ErrorCodes.ParameterReadOnly,
                    "Parameter '" + name + "' is read-only.", false);
            }

            return matches[0];
        }

        private static object ConvertValue(Parameter param, JsonValue value, string unit, ExternalUnit planUnits)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    return value.AsString(string.Empty);

                case StorageType.Integer:
                    // Yes/No parameters use integer 0/1.
                    if (value.IsBoolean)
                    {
                        return value.AsBool() ? 1 : 0;
                    }

                    return value.AsInt(0);

                case StorageType.Double:
                {
                    double d = value.AsDouble(double.NaN);
                    if (double.IsNaN(d))
                    {
                        throw new AgentException(ErrorCodes.ParameterTypeMismatch,
                            "Expected a number for double parameter '" + param.Definition?.Name + "'.", true);
                    }

                    // Length/Area/Angle units need conversion.
                    string unitType = param.Definition?.GetDataType()?.TypeId ?? string.Empty;
                    // Revit 2024 unit spec ids: e.g. autodesk.spec.aec:length-1, area-1, angle-1
                    if (unitType.Contains("Length") || unitType.Contains("UT_Length"))
                    {
                        // If the item specifies a unit, use it; otherwise use the plan's unit.
                        ExternalUnit eu = planUnits;
                        if (!string.IsNullOrEmpty(unit) && UnitNames.TryParseLength(unit, out eu))
                        {
                            // Use the item's unit.
                        }

                        return UnitNames.ToFeet(d, eu);
                    }

                    if (unitType.Contains("Area") || unitType.Contains("UT_Area"))
                    {
                        // Area in square feet = value * (scale)^2 / (304.8)^2
                        double scale = UnitNames.FeetPerUnit(planUnits);
                        return d * scale * scale;
                    }

                    if (unitType.Contains("Angle") || unitType.Contains("UT_Angle"))
                    {
                        // Convert degrees to radians.
                        string angleUnit = unit ?? "deg";
                        return UnitNames.ToRadians(d, angleUnit);
                    }

                    return d;
                }

                case StorageType.ElementId:
                {
                    long id = value.AsLong(0);
                    return new ElementId(id);
                }

                default:
                    throw new AgentException(ErrorCodes.ParameterTypeMismatch,
                        "Unsupported storage type " + param.StorageType + " for parameter '" + param.Definition?.Name + "'.", true);
            }
        }

        private static void ApplyValue(Parameter param, object value)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set((string)value);
                    break;
                case StorageType.Integer:
                    param.Set((int)value);
                    break;
                case StorageType.Double:
                    param.Set((double)value);
                    break;
                case StorageType.ElementId:
                    param.Set((ElementId)value);
                    break;
            }
        }

        private static string ReadParameterValue(Parameter p)
        {
            if (p == null || !p.HasValue)
            {
                return null;
            }

            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double: return p.AsDouble().ToString("0.######", CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                {
                    ElementId id = p.AsElementId();
                    return id != null ? id.Value.ToString(CultureInfo.InvariantCulture) : null;
                }
                default: return null;
            }
        }

        private sealed class ValidatedItem
        {
            public Element Target;
            public Parameter Parameter;
            public object Value;
            public int Index;
        }
    }
}
