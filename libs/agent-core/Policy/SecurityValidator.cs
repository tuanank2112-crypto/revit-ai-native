using System;
using System.Collections.Generic;
using System.IO;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Policy
{
    /// <summary>
    /// Validates file-system paths that arrive in plans (save_as, export.pdf, export.dwg)
    /// before any file I/O. Guards against traversal, drive-relative tricks, reserved
    /// device names, and paths outside configured export roots.
    /// </summary>
    public static class SecurityValidator
    {
        /// <summary>Result of a path check.</summary>
        public sealed class PathCheckResult
        {
            /// <summary>Creates a result.</summary>
            public PathCheckResult(bool allowed, AgentError error = null, string canonicalPath = null)
            {
                Allowed = allowed;
                Error = error;
                CanonicalPath = canonicalPath;
            }

            /// <summary>True when the path may be used.</summary>
            public bool Allowed { get; }

            /// <summary>Structured error when not allowed.</summary>
            public AgentError Error { get; }

            /// <summary>Canonicalized (full, normalized) form of the allowed path.</summary>
            public string CanonicalPath { get; }
        }

        /// <summary>
        /// Validates a destination path against the allowlisted export roots.
        /// Returns the canonicalized path when allowed.
        /// </summary>
        public static PathCheckResult ValidateExportPath(string path, IReadOnlyList<string> allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.InvalidArgument, "Export path is empty.", true, "Provide a non-empty path."));
            }

            string full = TryGetFullPath(path);
            if (full == null)
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.PathNotAllowed, "Export path cannot be normalized.", true, "Use an absolute path."));
            }

            if (IsDevicePath(full))
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.PathNotAllowed, "Device paths are not allowed for exports.", false));
            }

            if (allowedRoots == null || allowedRoots.Count == 0)
            {
                // No roots configured: deny by default. Safety is the default posture.
                return new PathCheckResult(false,
                    new AgentError(
                        ErrorCodes.PathNotAllowed,
                        "No export root directories are configured in the project policy.",
                        true,
                        "Add exportRootDirectories to config/project-policy.json."));
            }

            foreach (string rootText in allowedRoots)
            {
                string root = TryGetFullPath(rootText);
                if (root == null)
                {
                    continue;
                }

                if (IsWithin(root, full))
                {
                    return new PathCheckResult(true, null, full);
                }
            }

            return new PathCheckResult(false,
                new AgentError(
                    ErrorCodes.PathNotAllowed,
                    "Export path '" + path + "' is outside the configured export roots.",
                    true,
                    "Use a path under one of the configured exportRootDirectories."));
        }

        /// <summary>
        /// Validates a document save-as path. Same traversal/device checks as export,
        /// but save-as is intentionally not restricted to export roots.
        /// </summary>
        public static PathCheckResult ValidateSaveAsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.InvalidArgument, "Save-as path is empty.", true, "Provide a non-empty path."));
            }

            string full = TryGetFullPath(path);
            if (full == null)
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.PathNotAllowed, "Save-as path cannot be normalized.", true, "Use an absolute path."));
            }

            if (IsDevicePath(full))
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.PathNotAllowed, "Device paths are not allowed for save-as.", false));
            }

            string directory = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return new PathCheckResult(false,
                    new AgentError(ErrorCodes.PathNotAllowed, "The target directory does not exist.", true, "Create the directory first."));
            }

            return new PathCheckResult(true, null, full);
        }

        /// <summary>
        /// Detects path traversal in a relative path fragment (used by path-like arguments
        /// that must stay inside a container, e.g. import sources).
        /// </summary>
        public static bool ContainsTraversal(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (segment == "..")
                {
                    return true;
                }
            }

            // Also reject absolute and rooted forms outright.
            return Path.IsPathRooted(path) || IsDevicePath(path);
        }

        private static bool IsWithin(string root, string path)
        {
            string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;

            return path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
        }

        private static string TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsDevicePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalized = path.Trim();
            // \\?\ and \\.\ prefixes bypass normal path processing entirely.
            if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return true;
            }

            // Reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) with or without extension.
            string fileName = Path.GetFileNameWithoutExtension(normalized);
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            switch (fileName.ToUpperInvariant())
            {
                case "CON":
                case "PRN":
                case "AUX":
                case "NUL":
                case "COM1":
                case "COM2":
                case "COM3":
                case "COM4":
                case "COM5":
                case "COM6":
                case "COM7":
                case "COM8":
                case "COM9":
                case "LPT1":
                case "LPT2":
                case "LPT3":
                case "LPT4":
                case "LPT5":
                case "LPT6":
                case "LPT7":
                case "LPT8":
                case "LPT9":
                    return true;
                default:
                    return false;
            }
        }
    }
}
