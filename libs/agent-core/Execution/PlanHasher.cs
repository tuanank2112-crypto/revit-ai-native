using System;
using System.Security.Cryptography;
using System.Text;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Execution
{
    /// <summary>
    /// Hashes a plan deterministically. The same plan submitted with different JSON member
    /// order must produce the same hash, so the canonical writer is used. The hash protects
    /// the preview→commit handoff against a changed plan (see DOCUMENT_CHANGED_SINCE_PREVIEW
    /// and PLAN_HASH_MISMATCH).
    /// </summary>
    public static class PlanHasher
    {
        /// <summary>Computes the canonical SHA-256 hash of a plan document.</summary>
        public static string HashJson(JsonValue planJson)
        {
            if (planJson == null)
            {
                return EmptyHash();
            }

            string canonical = JsonWriter.WriteCanonical(planJson);
            using (var sha = SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
                byte[] hash = sha.ComputeHash(bytes);
                return ToHex(hash);
            }
        }

        /// <summary>Short (12-hex) prefix of the full hash, for human-readable logging.</summary>
        public static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return string.Empty;
            }

            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        /// <summary>SHA-256 of the empty string, used as a deterministic placeholder.</summary>
        public static string EmptyHash()
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Empty)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }
}
