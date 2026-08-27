using System;
using System.Security.Cryptography;
using System.Text;

namespace AutodeskNativeAgent.Core.Identity
{
    /// <summary>
    /// Computes a stable fingerprint of a Revit document. The fingerprint binds previews and
    /// executions to a document; a changed document between preview and commit is refused
    /// with DOCUMENT_CHANGED_SINCE_PREVIEW.
    /// </summary>
    /// <remarks>
    /// The identity half (title + path + project info) is cheap and always available. The
    /// optional content hash is only used when requested, since hashing a full model can be
    /// expensive on large files. Equality decisions must never depend on file-system paths,
    /// which are machine-specific.
    /// </remarks>
    public static class DocumentFingerprint
    {
        /// <summary>Builds a fingerprint from the document identity fields.</summary>
        public static string FromIdentity(string title, string path, string projectNumber, string projectName)
        {
            var sb = new StringBuilder(256);
            sb.Append("v1|");
            sb.Append(title ?? string.Empty);
            sb.Append('|');
            sb.Append(path ?? string.Empty);
            sb.Append('|');
            sb.Append(projectNumber ?? string.Empty);
            sb.Append('|');
            sb.Append(projectName ?? string.Empty);
            return Sha256Hex(sb.ToString());
        }

        /// <summary>Hashes arbitrary content (e.g. a serialized model snapshot).</summary>
        public static string HashContent(string content)
        {
            return Sha256Hex(content ?? string.Empty);
        }

        /// <summary>Combines multiple content hashes into one (for multi-stream documents).</summary>
        public static string Combine(params string[] hashes)
        {
            if (hashes == null || hashes.Length == 0)
            {
                return Sha256Hex(string.Empty);
            }

            var sb = new StringBuilder(256);
            foreach (string hash in hashes)
            {
                sb.Append(hash ?? string.Empty);
                sb.Append('|');
            }

            return Sha256Hex(sb.ToString());
        }

        private static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }
    }
}
