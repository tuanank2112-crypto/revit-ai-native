using System;
using AutodeskNativeAgent.Core.Contracts;
using Autodesk.Revit.DB;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Converts plan geometry and lengths between the plan's external units and Revit's
    /// internal decimal feet, and builds the coordinate transforms for each supported
    /// coordinate system.
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>Converts a plan length (in the plan's units) to internal feet.</summary>
        public static double ToFeet(double value, ExternalUnit unit) => UnitNames.ToFeet(value, unit);

        /// <summary>Converts internal feet to a plan unit.</summary>
        public static double FromFeet(double feet, ExternalUnit unit) => UnitNames.FromFeet(feet, unit);

        /// <summary>Inches per millimetre.</summary>
        private const double MmToInch = 1d / 25.4d;

        /// <summary>Maps plan point (x,y,z in plan units) into Revit internal coordinates.</summary>
        public static XYZ InternalPoint(double x, double y, double z, ExternalUnit unit)
        {
            double scale = UnitNames.FeetPerUnit(unit);
            return new XYZ(x * scale, y * scale, z * scale);
        }

        /// <summary>Gets the transform that converts plan coordinates into internal ones.</summary>
        public static Transform CoordinateTransform(Autodesk.Revit.DB.Document document, CoordinateSystem system)
        {
            switch (system)
            {
                case CoordinateSystem.Internal:
                    return Transform.Identity;

                case CoordinateSystem.Project:
                case CoordinateSystem.Shared:
                {
                    ProjectLocation location = document.ActiveProjectLocation;
                    if (location == null)
                    {
                        return Transform.Identity;
                    }

                    if (system == CoordinateSystem.Project)
                    {
                        return location.GetTransform();
                    }

                    return location.GetTransform(); // shared origin matches project in single-location projects
                }

                case CoordinateSystem.ActiveView:
                {
                    View view = document.ActiveView;
                    if (view == null)
                    {
                        return Transform.Identity;
                    }

                    return view.CropBox != null ? view.CropBox.Transform : Transform.Identity;
                }

                default:
                    return Transform.Identity;
            }
        }

        /// <summary>Identity passthrough kept for symmetry with the transform helpers.</summary>
        public static XYZ TransformPoint(XYZ planPoint)
        {
            // Plan points arrive in internal units; coordinate-system offsets were already
            // applied by CoordinateTransform when the plan demanded them.
            return planPoint;
        }
    }
}
