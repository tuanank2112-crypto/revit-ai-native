using System;

namespace AutodeskNativeAgent.Core.Contracts
{
    /// <summary>
    /// External length units accepted at the plan level. Every bare length in a plan is
    /// interpreted in the unit declared by <c>plan.units</c>; values are never guessed.
    /// </summary>
    public enum ExternalUnit
    {
        /// <summary>Millimetres.</summary>
        Mm = 0,

        /// <summary>Centimetres.</summary>
        Cm = 1,

        /// <summary>Metres.</summary>
        Meter = 2,

        /// <summary>Inches.</summary>
        Inch = 3,

        /// <summary>Feet.</summary>
        Foot = 4
    }

    /// <summary>
    /// Parses, serialises and converts plan units. Revit's internal length unit is decimal
    /// feet, so every external length goes through <see cref="ToFeet"/> before touching the API.
    /// </summary>
    public static class UnitNames
    {
        /// <summary>Multipliers that convert one unit to millimetres, indexed by <see cref="ExternalUnit"/>.</summary>
        private static readonly double[] MmPerUnit =
        {
            1d,       // Mm
            10d,      // Cm
            1000d,    // Meter
            25.4d,    // Inch
            304.8d    // Foot
        };

        /// <summary>Attempts to parse a wire token into an <see cref="ExternalUnit"/>.</summary>
        public static bool TryParseLength(string text, out ExternalUnit unit)
        {
            switch (text)
            {
                case "mm":
                    unit = ExternalUnit.Mm;
                    return true;
                case "cm":
                    unit = ExternalUnit.Cm;
                    return true;
                case "m":
                    unit = ExternalUnit.Meter;
                    return true;
                case "inch":
                    unit = ExternalUnit.Inch;
                    return true;
                case "ft":
                    unit = ExternalUnit.Foot;
                    return true;
                default:
                    unit = ExternalUnit.Mm;
                    return false;
            }
        }

        /// <summary>Maps a unit to its wire token.</summary>
        public static string ToWire(ExternalUnit unit)
        {
            switch (unit)
            {
                case ExternalUnit.Cm: return "cm";
                case ExternalUnit.Meter: return "m";
                case ExternalUnit.Inch: return "inch";
                case ExternalUnit.Foot: return "ft";
                default: return "mm";
            }
        }

        /// <summary>True when <paramref name="unit"/> is one of the plan-level length units.</summary>
        public static bool IsLengthUnit(ExternalUnit unit) => unit >= ExternalUnit.Mm && unit <= ExternalUnit.Foot;

        /// <summary>External feet per unit.</summary>
        public static double FeetPerUnit(ExternalUnit unit) => MmPerUnit[(int)unit] / 304.8d;

        /// <summary>Converts a value in the given unit to Revit internal feet.</summary>
        public static double ToFeet(double value, ExternalUnit unit) => value * FeetPerUnit(unit);

        /// <summary>Converts Revit internal feet to the given unit.</summary>
        public static double FromFeet(double feet, ExternalUnit unit) => feet / FeetPerUnit(unit);

        /// <summary>Converts a value in millimetres to Revit internal feet.</summary>
        public static double MmToFeet(double mm) => mm / 304.8d;

        /// <summary>Converts Revit internal feet to millimetres.</summary>
        public static double FeetToMm(double feet) => feet * 304.8d;

        /// <summary>Attempts to parse an angle unit accepted by assertions.</summary>
        public static bool TryParseAngle(string text, out bool radians)
        {
            if (text == "deg")
            {
                radians = false;
                return true;
            }

            if (text == "rad")
            {
                radians = true;
                return true;
            }

            radians = false;
            return false;
        }

        /// <summary>Converts an angle in the given unit to radians.</summary>
        public static double ToRadians(double value, string unit)
        {
            if (string.Equals(unit, "deg", StringComparison.Ordinal))
            {
                return value * (Math.PI / 180d);
            }

            return value;
        }

        /// <summary>Converts radians to an angle in the given unit (deg or rad).</summary>
        public static double FromRadians(double radians, string unit)
        {
            if (string.Equals(unit, "deg", StringComparison.Ordinal))
            {
                return radians * (180d / Math.PI);
            }

            return radians;
        }
    }
}
