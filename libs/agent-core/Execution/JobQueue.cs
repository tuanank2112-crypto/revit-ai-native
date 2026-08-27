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
                return _registry.Remove(jobId);
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
