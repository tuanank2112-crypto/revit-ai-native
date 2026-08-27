using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Execution
{
    /// <summary>
    /// A job in the execution queue. Tracks state transitions, the plan, the preview
    /// result, the confirmation token and the final execution result. Thread-safe.
    /// </summary>
    public sealed class Job
    {
        private readonly object _gate = new object();
        private JobStatus _status;
        private ExecutionResult _result;
        private string _confirmationToken;

        /// <summary>Creates a job.</summary>
        public Job(string jobId, AgentPlan plan, string planHash, string submittedBy)
        {
            JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            PlanHash = planHash ?? string.Empty;
            SubmittedBy = submittedBy ?? string.Empty;
            SubmittedAtUtc = DateTime.UtcNow;
            _status = JobStatus.Queued;
        }

        /// <summary>Server-assigned job id.</summary>
        public string JobId { get; }

        /// <summary>The plan this job runs.</summary>
        public AgentPlan Plan { get; }

        /// <summary>Canonical hash of the plan.</summary>
        public string PlanHash { get; }

        /// <summary>Who submitted the job (actor for audit).</summary>
        public string SubmittedBy { get; }

        /// <summary>When the job was submitted.</summary>
        public DateTime SubmittedAtUtc { get; }

        /// <summary>Current status.</summary>
        public JobStatus Status
        {
            get
            {
                lock (_gate)
                {
                    return _status;
                }
            }
        }

        /// <summary>The final result, set when a terminal state is reached.</summary>
        public ExecutionResult Result
        {
            get
            {
                lock (_gate)
                {
                    return _result;
                }
            }
        }

        /// <summary>The confirmation token issued for this job, if any.</summary>
        public string ConfirmationToken
        {
            get
            {
                lock (_gate)
                {
                    return _confirmationToken;
                }
            }
        }

        /// <summary>True when the job is in a terminal state.</summary>
        public bool IsTerminal
        {
            get
            {
                lock (_gate)
                {
                    return _status == JobStatus.Completed ||
                           _status == JobStatus.Failed ||
                           _status == JobStatus.Cancelled ||
                           _status == JobStatus.RolledBack ||
                           _status == JobStatus.TimedOut;
                }
            }
        }

        /// <summary>True when the job can still be cancelled.</summary>
        public bool IsCancellable
        {
            get
            {
                lock (_gate)
                {
                    return _status == JobStatus.Queued ||
                           _status == JobStatus.WaitingForRevit ||
                           _status == JobStatus.Validating ||
                           _status == JobStatus.AwaitingConfirmation;
                }
            }
        }

        /// <summary>Transitions the job to a new status. Returns the previous status.</summary>
        public JobStatus Transition(JobStatus newStatus)
        {
            lock (_gate)
            {
                if (!JobStateMachine.IsValidTransition(_status, newStatus))
                {
                    throw new InvalidOperationException(
                        "Invalid job state transition from " +
                        ExecutionResult.StatusToWire(_status) + " to " +
                        ExecutionResult.StatusToWire(newStatus) + ".");
                }

                JobStatus old = _status;
                _status = newStatus;
                return old;
            }
        }

        /// <summary>Sets the confirmation token for this job.</summary>
        public void SetConfirmationToken(string token)
        {
            lock (_gate)
            {
                _confirmationToken = token;
            }
        }

        /// <summary>Sets the final result and transitions to the matching terminal state.</summary>
        public void Complete(ExecutionResult result)
        {
            lock (_gate)
            {
                _result = result;
                _status = result.Status;
            }
        }

        /// <summary>Returns a lightweight status snapshot.</summary>
        public JsonValue ToStatusJson()
        {
            lock (_gate)
            {
                var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["jobId"] = JsonValue.String(JobId),
                    ["status"] = JsonValue.String(ExecutionResult.StatusToWire(_status)),
                    ["planHash"] = JsonValue.String(PlanHash),
                    ["submittedAtUtc"] = JsonValue.String(SubmittedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
                };

                if (_result != null)
                {
                    members["result"] = _result.ToJson();
                }

                return JsonValue.Object(members);
            }
        }
    }

    /// <summary>
    /// Enforces the legal state machine for a plan job. Transitions not listed in
    /// <see cref="IsValidTransition"/> are refused by the dispatcher.
    /// </summary>
    public static class JobStateMachine
    {
        /// <summary>Returns true when the transition is legal.</summary>
        public static bool IsValidTransition(JobStatus from, JobStatus to)
        {
            // No transition needed when staying put.
            if (from == to)
            {
                return true;
            }

            switch (from)
            {
                case JobStatus.Queued:
                    return to == JobStatus.WaitingForRevit ||
                           to == JobStatus.Cancelled ||
                           to == JobStatus.Failed;

                case JobStatus.WaitingForRevit:
                    return to == JobStatus.Validating ||
                           to == JobStatus.Cancelled ||
                           to == JobStatus.Failed ||
                           to == JobStatus.TimedOut;

                case JobStatus.Validating:
                    return to == JobStatus.AwaitingConfirmation ||
                           to == JobStatus.Executing ||
                           to == JobStatus.Failed ||
                           to == JobStatus.Cancelled;

                case JobStatus.AwaitingConfirmation:
                    return to == JobStatus.Executing ||
                           to == JobStatus.Cancelled ||
                           to == JobStatus.Failed ||
                           to == JobStatus.TimedOut;

                case JobStatus.Executing:
                    return to == JobStatus.Verifying ||
                           to == JobStatus.Failed ||
                           to == JobStatus.RolledBack ||
                           to == JobStatus.Cancelled;

                case JobStatus.Verifying:
                    return to == JobStatus.Completed ||
                           to == JobStatus.Failed ||
                           to == JobStatus.RolledBack ||
                           to == JobStatus.Cancelled;

                // Terminal states: no outgoing transitions.
                default:
                    return false;
            }
        }
    }
}
