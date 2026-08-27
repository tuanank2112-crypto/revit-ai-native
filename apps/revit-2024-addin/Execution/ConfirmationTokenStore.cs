using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Policy;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// In-memory store for confirmation tokens. Only one token is active per job;
    /// tokens are consumed (Used) after the plan commits and expire automatically.
    /// </summary>
    public sealed class ConfirmationTokenStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, ConfirmationToken> _byJob =
            new Dictionary<string, ConfirmationToken>(StringComparer.Ordinal);

        /// <summary>Issues a new token for a job.</summary>
        public ConfirmationToken Issue(string jobId, string planHash, string description)
        {
            var token = new ConfirmationToken(jobId, planHash, description);

            lock (_gate)
            {
                // Replace any existing pending token for this job.
                _byJob[jobId] = token;
            }

            return token;
        }

        /// <summary>Gets the token for a job, or null.</summary>
        public ConfirmationToken Get(string jobId)
        {
            lock (_gate)
            {
                ConfirmationToken token;
                return _byJob.TryGetValue(jobId, out token) ? token : null;
            }
        }

        /// <summary>Removes the token for a job.</summary>
        public void Remove(string jobId)
        {
            lock (_gate)
            {
                _byJob.Remove(jobId);
            }
        }

        /// <summary>Expires all tokens older than the given threshold. Called periodically.</summary>
        public void SweepExpired()
        {
            lock (_gate)
            {
                var toRemove = new List<string>();
                foreach (var pair in _byJob)
                {
                    if (pair.Value.State != ConfirmationState.Pending &&
                        pair.Value.State != ConfirmationState.Accepted)
                    {
                        toRemove.Add(pair.Key);
                    }
                    else if (DateTime.UtcNow > pair.Value.ExpiresAtUtc)
                    {
                        toRemove.Add(pair.Key);
                    }
                }

                foreach (string jobId in toRemove)
                {
                    _byJob.Remove(jobId);
                }
            }
        }
    }
}
