using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Revit2024.Execution.Operations
{
    /// <summary>Executes <c>parameter.set</c>: writes a typed value to a parameter.</summary>
    public static class ParameterSetOperation
    {
        /// <summary>Executes the operation inside an active transaction.</summary>
        public static OperationResult Execute(
            Document document,
            PlanOperation operation,
            AgentPlan plan,
            IReadOnlyDictionary<string, JsonValue> results)
        {
            JsonValue args = operation.Args;
            Element target = ElementResolver.Resolve(document, args["target"], results);
            JsonValue parameter = args["parameter"];
            JsonValue value = args["value"];

            bool acknowledgeSharedTypeImpact = args["acknowledgeSharedTypeImpact"].AsBool(false);
            string scope = parameter["scope"].AsString("instance");

            Parameter parameterInstance;
            if (scope == "type")
            {
                ElementType type = target.Document.GetElement(target.GetTypeId()) as ElementType;
                if (type == null)
                {
                    throw new AgentException(ErrorCodes.ParameterNotFound, "The element has no type to write.", true);
                }

                if (!acknowledgeSharedTypeImpact)
                {
                    throw new AgentException(
                        ErrorCodes.ParameterReadOnly,
                        "Writing a type parameter affects all instances; set acknowledgeSharedTypeImpact=true to proceed.",
                        true,
                        "Set acknowledgeSharedTypeImpact to true.");
                }

                parameterInstance = ResolveParameter(type, parameter);
                if (parameterInstance == null)
                {
                    throw new AgentException(ErrorCodes.ParameterNotFound,
                        "No parameter '" + Describe(parameter) + "' on type " + type.Name + ".", true);
                }

                Apply(target, parameterInstance, value, operation, plan);
                return new OperationResult(
                    operation.Id,
                    "parameter.set",
                    OperationOutcome.Completed,
                    modified: new[] { ElementResolver.Summarize(target) });
            }

            parameterInstance = ResolveParameter(target, parameter);
            if (parameterInstance == null)
            {
                throw new AgentException(ErrorCodes.ParameterNotFound,
                    "No parameter '" + Describe(parameter) + "' on element " + target.Id + ".", true);
            }

            Apply(target, parameterInstance, value, operation, plan);
            return new OperationResult(
                operation.Id,
                "parameter.set",
                OperationOutcome.Completed,
                modified: new[] { ElementResolver.Summarize(target) });
        }

        private static Parameter ResolveParameter(Element element, JsonValue parameter)
        {
            string builtInText = parameter["builtIn"].AsString(null);
            if (builtInText != null)
            {
                BuiltInParameter bip;
                if (Enum.TryParse(builtInText, true, out bip) && Enum.IsDefined(typeof(BuiltInParameter), bip))
                {
                    return element.get_Parameter(bip);
                }

                return null;
            }

            string guid = parameter["guid"].AsString(null);
            if (guid != null)
            {
                Guid g;
                if (Guid.TryParse(guid, out g))
                {
                    return element.get_Parameter(g);
                }

                return null;
            }

            string name = parameter["name"].AsString(null);
            if (name != null)
            {
                return element.LookupParameter(name);
            }

            return null;
        }

        private static void Apply(Element element, Parameter parameter, JsonValue value, PlanOperation operation, AgentPlan plan)
        {
            if (!parameter.IsReadOnly)
            {
                string kind = value["kind"].AsString(null);
                switch (kind)
                {
                    case "string":
                        parameter.Set(value["string"].AsString(string.Empty));
                        break;
                    case "integer":
                        parameter.Set(value["integer"].AsInt(0));
                        break;
                    case "double":
                        parameter.Set(value["double"].AsDouble(0));
                        break;
                    case "boolean":
                        parameter.Set(value["boolean"].AsBool(false) ? 1 : 0);
                        break;
                    case "elementId":
                        parameter.Set(new ElementId(value["elementId"].AsLong(0)));
                        break;
                    case "length":
                        parameter.Set(ConvertLength(value["number"].AsDouble(0), value["unit"].AsString(null), plan.Units));
                        break;
                    case "area":
                        parameter.Set(ConvertArea(value["number"].AsDouble(0), value["unit"].AsString(null)));
                        break;
                    case "angle":
                        parameter.Set(ConvertAngle(value["number"].AsDouble(0), value["unit"].AsString(null)));
                        break;
                    default:
                        throw new AgentException(ErrorCodes.ParameterTypeMismatch,
                            "Unsupported value kind '" + (kind ?? "<missing>") + "'.", true);
                }

                // Mark parameter writes that affect a type used by multiple instances.
                if (parameter.IsShared && parameter.IsReadOnly == false)
                {
                    // no-op: shared parameters write through the same API path
                }
            }
            else
            {
                throw new AgentException(ErrorCodes.ParameterReadOnly,
                    "Parameter '" + parameter.Definition?.Name + "' is read-only.", true);
            }
        }

        private static double ConvertLength(double value, string unit, ExternalUnit planUnits)
        {
            if (string.IsNullOrEmpty(unit))
            {
                // Unit omitted: interpret in the plan's units.
                return UnitNames.ToFeet(value, planUnits);
            }

            ExternalUnit parsed;
            if (UnitNames.TryParseLength(unit, out parsed))
            {
                return UnitNames.ToFeet(value, parsed);
            }

            throw new AgentException(ErrorCodes.UnitUnsupported, "Unsupported length unit '" + unit + "'.", true);
        }

        private static double ConvertArea(double value, string unit)
        {
            switch (unit)
            {
                case "mm2": return value / (304.8 * 304.8);
                case "m2": return value / (0.3048 * 0.3048);
                case "ft2": return value;
                default: return value; // internal square feet assumed
            }
        }

        private static double ConvertAngle(double value, string unit)
        {
            if (unit == "deg")
            {
                return value * (Math.PI / 180d);
            }

            if (unit == "rad")
            {
                return value;
            }

            return value; // internal radians assumed
        }

        private static string Describe(JsonValue parameter)
        {
            string builtIn = parameter["builtIn"].AsString(null);
            if (builtIn != null)
            {
                return "builtIn:" + builtIn;
            }

            string guid = parameter["guid"].AsString(null);
            if (guid != null)
            {
                return "guid:" + guid;
            }

            string name = parameter["name"].AsString(null);
            if (name != null)
            {
                return "name:" + name;
            }

            return "<unidentified>";
        }
    }
}
