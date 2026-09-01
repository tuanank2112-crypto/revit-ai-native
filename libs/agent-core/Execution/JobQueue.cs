using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AutodeskNativeAgent.Core.Contracts;

namespace AutodeskNativeAgent.Core.Execution
{
    /// <summary>
    /// A thread-safe queue of plan jobs. The MCP server enqueues here; the Revit
    /// add-in's dispatcher dequeues one at a time on the main thread.
    /// </summary>
    public sealed class JobQueue
    {
        private readonly BlockingCollection<Job> _queue = new BlockingCollection<Job>(64);
        private readonly object _registryGate = new object();
        private readonly Dictionary<string, Job> _registry = new Dictionary<string, Job>(StringComparer.Ordinal);
        private readonly Dictionary<string, Job> _idempotencyRegistry = new Dictionary<string, Job>(StringComparer.Ordinal);

        /// <summary>Enqueues a job. Returns false when the queue is full.</summary>
        public bool Enqueue(Job job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (!_queue.TryAdd(job, TimeSpan.FromSeconds(2)))
            {
                return false;
            }

            lock (_registryGate)
            {
                _registry[job.JobId] = job;
            }

            return true;
        }

        /// <summary>
        /// Atomically returns an existing job for the same request identity, or enqueues
        /// and registers the supplied job. The plan hash prevents accidental reuse when
        /// a caller reuses a request id for different content.
        /// </summary>
        public bool EnqueueIdempotent(Job job, string requestIdentity, string planHash, out Job existingJob)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            string key = BuildIdempotencyKey(requestIdentity, planHash);
            lock (_registryGate)
            {
                if (_idempotencyRegistry.TryGetValue(key, out existingJob))
                {
                    return true;
                }

                if (!_queue.TryAdd(job, TimeSpan.FromSeconds(2)))
                {
                    existingJob = null;
                    return false;
                }

                _registry[job.JobId] = job;
                _idempotencyRegistry[key] = job;
                existingJob = null;
                return true;
            }
        }

        /// <summary>Finds a job by its request identity and exact plan hash.</summary>
        public Job FindByIdempotency(string requestIdentity, string planHash)
        {
            lock (_registryGate)
            {
                Job job;
                return _idempotencyRegistry.TryGetValue(BuildIdempotencyKey(requestIdentity, planHash), out job)
                    ? job
                    : null;
            }
        }

        /// <summary>Finds an existing job for a request identity, regardless of plan hash.</summary>
        public Job FindByRequestIdentity(string requestIdentity)
        {
            string prefix = (requestIdentity ?? string.Empty).Trim() + "\n";
            lock (_registryGate)
            {
                foreach (var pair in _idempotencyRegistry)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return pair.Value;
                    }
                }
            }

            return null;
        }

        private static string BuildIdempotencyKey(string requestIdentity, string planHash)
        {
            return (requestIdentity ?? string.Empty).Trim() + "\n" + (planHash ?? string.Empty).Trim();
        }

        /// <summary>Dequeues the next job, blocking up to <paramref name="timeoutMs"/>.</summary>
        public Job Dequeue(int timeoutMs)
        {
            Job job;
            if (!_queue.TryTake(out job, timeoutMs))
            {
                return null;
            }

            return job;
        }

        /// <summary>Tries to find a job by id.</summary>
        public Job Find(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return null;
            }

            lock (_registryGate)
            {
                Job job;
                return _registry.TryGetValue(jobId, out job) ? job : null;
            }
        }

        /// <summary>Returns a snapshot of all known jobs.</summary>
        public IReadOnlyList<Job> All
        {
            get
            {
                lock (_registryGate)
                {
                    var list = new List<Job>(_registry.Values);
                    return list;
                }
            }
        }

        /// <summary>Removes a job from the registry (called after the result is collected).</summary>
        public bool Remove(string jobId)
        {
            lock (_registryGate)
            {
                Job removed;
                if (!_registry.TryGetValue(jobId, out removed))
                {
                    return false;
                }

                _registry.Remove(jobId);
                var keysToRemove = new List<string>();
                foreach (var pair in _idempotencyRegistry)
                {
                    if (ReferenceEquals(pair.Value, removed))
                    {
                        keysToRemove.Add(pair.Key);
                    }
                }

                foreach (string key in keysToRemove)
                {
                    _idempotencyRegistry.Remove(key);
                }

                return true;
            }
        }

        /// <summary>Number of jobs currently tracked.</summary>
        public int Count
        {
            get
            {
                lock (_registryGate)
                {
                    return _registry.Count;
                }
            }
        }

        /// <summary>Completes the queue; no more items can be enqueued.</summary>
        public void CompleteAdding()
        {
            _queue.CompleteAdding();
        }
    }
}
