using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Execution;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Core.Validation;
using AutodeskNativeAgent.Revit2024.Execution.Operations;
using AutodeskNativeAgent.Revit2024.Pipe;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// Routes incoming pipe requests to the correct handler. Read operations run
    /// immediately on the main thread via the dispatcher; plan operations enqueue
    /// a job and return immediately with the job id.
    /// </summary>
    public sealed class AgentRequestRouter
    {
        private readonly UIControlledApplication _app;
        private readonly MainThreadDispatcher _dispatcher;
        private readonly JobQueue _jobQueue;
        private readonly AuditLog _auditLog;
        private readonly ConfirmationTokenStore _tokenStore;
        private readonly CommandRegistry _registry;

        private string _activeDocumentTitle = "<no document>";
        private string _activeDocumentPath = string.Empty;

        /// <summary>Creates the router.</summary>
        public AgentRequestRouter(
            UIControlledApplication app,
            MainThreadDispatcher dispatcher,
            JobQueue jobQueue,
            AuditLog auditLog,
            ConfirmationTokenStore tokenStore)
        {
            _app = app;
            _dispatcher = dispatcher;
            _jobQueue = jobQueue;
            _auditLog = auditLog;
            _tokenStore = tokenStore;
            _registry = CommandRegistry.CreateDefault();
        }

        /// <summary>Handles one pipe request and returns a response envelope.</summary>
        public JsonValue HandleRequest(JsonValue request, string requestId)
        {
            try
            {
                string method = request["method"].AsString(null);
                if (string.IsNullOrEmpty(method))
                {
                    return PipeProtocol.Fail(requestId, null,
                        new AgentError(ErrorCodes.MalformedMessage, "Request is missing 'method'.", true));
                }

                JsonValue payload = request["payload"].IsNull ? JsonValue.EmptyObject() : request["payload"];

                // Read operations and connection lifecycle are handled synchronously on the
                // main thread via the dispatcher. Plan operations enqueue and return a job id.
                switch (method)
                {
                    case PipeProtocol.TypeHeartbeat:
                        // Heartbeat: acknowledge without dispatching to an operation.
                        return PipeProtocol.Ok(requestId, method, JsonValue.EmptyObject());

                    case ProtocolCatalog.Hello:
                        return PipeProtocol.Ok(requestId, method, BuildHelloData());

                    case ProtocolCatalog.Status:
                        return BuildStatusData(requestId);

                    case ProtocolCatalog.Capabilities:
                        return PipeProtocol.Ok(requestId, method, BuildCapabilitiesData());

                    case ProtocolCatalog.PlanPreview:
                    case ProtocolCatalog.PlanValidate:
                    case ProtocolCatalog.PlanCommit:
                        return HandlePlanMethod(method, requestId, payload);

                    case ProtocolCatalog.PlanConfirm:
                        return HandleConfirm(requestId, payload);

                    case ProtocolCatalog.JobStatus:
                        return HandleJobStatus(requestId, payload);

                    case ProtocolCatalog.JobCancel:
                        return HandleJobCancel(requestId, payload);

                    case ProtocolCatalog.JobRollback:
                        return HandleJobRollback(requestId, payload);

                    case ProtocolCatalog.AuditLog:
                        return HandleAuditLog(requestId, payload);

                    default:
                        // Read operations and element operations dispatch to the main thread.
                        return DispatchAndRun(method, requestId, payload);
                }
            }
            catch (AgentException ex)
            {
                return PipeProtocol.Fail(requestId, request["method"].AsString(null),
                    new AgentError(ex.Code, ex.Message, ex.Recoverable, ex.SuggestedAction));
            }
            catch (Exception ex)
            {
                _auditLog.Append("system", "router.exception", AuditSeverity.Error,
                    "Unhandled exception: " + ex.Message);
                return PipeProtocol.Fail(requestId, request["method"].AsString(null),
                    new AgentError(ErrorCodes.InternalError, "Internal error: " + ex.Message, false));
            }
        }

        private JsonValue BuildHelloData()
        {
            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["protocolVersion"] = JsonValue.String(PipeProtocol.Version),
                ["addInVersion"] = JsonValue.String("1.0.0"),
                ["capabilities"] = BuildCapabilitiesData()
            });
        }

        private JsonValue BuildStatusData(string requestId)
        {
            // The pipe handler thread cannot touch the Revit API; marshal to the main
            // thread so the active document title/path are read live, not from the
            // cache that only plan execution refreshes.
            var done = new System.Threading.AutoResetEvent(false);
            JsonValue[] resultHolder = new JsonValue[1];

            _dispatcher.Enqueue(new MainThreadWorkItem((app, doc) =>
            {
                try
                {
                    _activeDocumentTitle = doc != null ? doc.Title ?? string.Empty : string.Empty;
                    _activeDocumentPath = doc != null ? doc.PathName ?? string.Empty : string.Empty;
                    resultHolder[0] = PipeProtocol.Ok(
                        requestId,
                        ProtocolCatalog.Status,
                        BuildStatusData());
                }
                catch (Exception ex)
                {
                    resultHolder[0] = PipeProtocol.Fail(requestId, ProtocolCatalog.Status,
                        new AgentError(ErrorCodes.InternalError, "Status failed: " + ex.Message, false));
                }
                finally
                {
                    done.Set();
                }
            }, "Status"));

            done.WaitOne(TimeSpan.FromSeconds(10));
            return resultHolder[0] ?? PipeProtocol.Fail(requestId, ProtocolCatalog.Status,
                new AgentError(ErrorCodes.RequestTimeout, "Status timed out.", true));
        }

        private JsonValue BuildStatusData()
        {
            string docTitle = _activeDocumentTitle;
            string docPath = _activeDocumentPath;
            bool busy = _dispatcher.HasPendingWork || _jobQueue.Count > 0;

            if (string.IsNullOrEmpty(docTitle))
            {
                docTitle = "<no document>";
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["connected"] = JsonValue.Bool(true),
                ["busy"] = JsonValue.Bool(busy),
                ["activeDocumentTitle"] = JsonValue.String(docTitle),
                ["activeDocumentPath"] = JsonValue.String(docPath ?? string.Empty),
                ["queueDepth"] = JsonValue.Number(_jobQueue.Count)
            });
        }

        private JsonValue BuildCapabilitiesData()
        {
            var methods = new List<JsonValue>();
            foreach (ProtocolMethod method in ProtocolCatalog.All)
            {
                methods.Add(method.ToJson());
            }

            var operations = new List<JsonValue>();
            foreach (OperationDescriptor op in _registry.All)
            {
                operations.Add(JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["op"] = JsonValue.String(op.Op),
                    ["summary"] = JsonValue.String(op.Summary),
                    ["creates"] = JsonValue.Number(op.Creates),
                    ["modifies"] = JsonValue.Number(op.Modifies),
                    ["deletes"] = JsonValue.Number(op.Deletes),
                    ["supportedInPreview"] = JsonValue.Bool(op.SupportedInPreview),
                    ["supportedInCommit"] = JsonValue.Bool(op.SupportedInCommit)
                }));
            }

            return JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["protocolVersion"] = JsonValue.String(PipeProtocol.Version),
                ["methods"] = JsonValue.Array(methods),
                ["operations"] = JsonValue.Array(operations)
            });
        }

        private JsonValue HandlePlanMethod(string method, string requestId, JsonValue payload)
        {
            AgentPlan plan = AgentPlan.FromJson(payload, out AgentError error);
            if (error != null)
            {
                return PipeProtocol.Fail(requestId, method, error);
            }

            string planHash = PlanHasher.HashJson(payload);

            // Validate structurally before enqueueing.
            PlanValidationResult validation = PlanValidator.Validate(plan, _registry);
            if (!validation.Valid)
            {
                return PipeProtocol.Fail(requestId, method,
                    validation.Errors[0]);
            }

            if (method == ProtocolCatalog.PlanValidate)
            {
                // Validation-only: return the result immediately without enqueueing.
                return PipeProtocol.Ok(requestId, method,
                    JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                    {
                        ["valid"] = JsonValue.Bool(true),
                        ["planHash"] = JsonValue.String(planHash),
                        ["estimatedAffectedElements"] = JsonValue.Number(validation.EstimatedAffectedElements)
                    }));
            }

            string jobId = "j" + DateTime.UtcNow.Ticks.ToString("x");
            var job = new Job(jobId, plan, planHash, "mcp:" + requestId);

            if (!_jobQueue.Enqueue(job))
            {
                return PipeProtocol.Fail(requestId, method,
                    new AgentError(ErrorCodes.RateLimited, "The job queue is full.", true, "Retry shortly."));
            }

            job.Transition(JobStatus.WaitingForRevit);

            _auditLog.Append("mcp:" + requestId, method, AuditSeverity.Action,
                "Plan enqueued: " + plan.Description, null, planHash, jobId);

            // Enqueue the actual plan execution on the main thread.
            _dispatcher.Enqueue(new MainThreadWorkItem(
                (app, doc) => ExecutePlan(app, doc, job, method),
                "Execute plan " + jobId));

            return PipeProtocol.Ok(requestId, method,
                JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["jobId"] = JsonValue.String(jobId),
                    ["planHash"] = JsonValue.String(planHash),
                    ["status"] = JsonValue.String(ExecutionResult.StatusToWire(job.Status))
                }));
        }

        private void ExecutePlan(UIApplication app, Document doc, Job job, string method)
        {
            if (doc != null)
            {
                _activeDocumentTitle = doc.Title ?? string.Empty;
                _activeDocumentPath = doc.PathName ?? string.Empty;
            }

            if (doc == null)
            {
                job.Complete(new ExecutionResult(
                    job.JobId, JobStatus.Failed, string.Empty, job.PlanHash,
                    DateTime.UtcNow, DateTime.UtcNow, true, null, null, null,
                    new[] { new AgentError(ErrorCodes.NoActiveDocument, "No active document.", true) }));
                return;
            }

            bool isPreview = method == ProtocolCatalog.PlanPreview;
            bool isCommit = method == ProtocolCatalog.PlanCommit;

            try
            {
                // First run: WaitingForRevit -> Validating. Resume after the user
                // accepted a confirmation: the job is in AwaitingConfirmation and the
                // state machine only allows AwaitingConfirmation -> Executing, so the
                // Validating step must be skipped on that path.
                if (job.Status == JobStatus.WaitingForRevit)
                {
                    job.Transition(JobStatus.Validating);
                }

                var executor = new PlanExecutor();
                if (isPreview)
                {
                    PreviewReport report = executor.Preview(doc, job.Plan, _registry);
                    job.Complete(new ExecutionResult(
                        job.JobId, JobStatus.Completed, report.DocumentFingerprint, job.PlanHash,
                        DateTime.UtcNow, DateTime.UtcNow, true, null, null, null, null));
                }
                else if (isCommit)
                {
                    // Confirmation check: if the plan requires confirmation, we need a valid token.
                    if (job.Plan.Safety.RequireUserConfirmation)
                    {
                        ConfirmationToken token = _tokenStore.Get(job.JobId);
                        if (token == null)
                        {
                            // Issue a token and stop; the agent must confirm before retrying.
                            token = _tokenStore.Issue(job.JobId, job.PlanHash, job.Plan.Description);
                            job.SetConfirmationToken(token.Value);
                            job.Transition(JobStatus.AwaitingConfirmation);
                            return;
                        }

                        // Validate the token carried on the job (set by HandleConfirm or by the
                        // agent echoing the preview token). If the job has no token, fail.
                        string tokenValue = job.ConfirmationToken;
                        if (string.IsNullOrEmpty(tokenValue))
                        {
                            job.Complete(new ExecutionResult(
                                job.JobId, JobStatus.AwaitingConfirmation, string.Empty, job.PlanHash,
                                DateTime.UtcNow, DateTime.UtcNow, true, null, null, null,
                                new[] { new AgentError(ErrorCodes.ConfirmationRequired,
                                    "This plan requires confirmation. Accept the pending confirmation or provide the token.", true) }));
                            return;
                        }

                        AgentError tokenError = token.Validate(tokenValue);
                        if (tokenError != null)
                        {
                            job.Complete(new ExecutionResult(
                                job.JobId, JobStatus.Failed, string.Empty, job.PlanHash,
                                DateTime.UtcNow, DateTime.UtcNow, true, null, null, null,
                                new[] { tokenError }));
                            return;
                        }

                        token.Accept();
                    }

                    ExecutionResult result = executor.Commit(doc, job.Plan, _registry);
                    job.Complete(result);
                    _auditLog.Append("mcp", "plan.commit", AuditSeverity.Action,
                        "Plan committed: " + job.Plan.Description, null, job.PlanHash, job.JobId);
                }
            }
            catch (AgentException ex)
            {
                job.Complete(new ExecutionResult(
                    job.JobId, JobStatus.Failed, string.Empty, job.PlanHash,
                    DateTime.UtcNow, DateTime.UtcNow, true, null, null,
                    new RollbackInfo(false, ex.Message),
                    new[] { new AgentError(ex.Code, ex.Message, ex.Recoverable, ex.SuggestedAction) }));
            }
            catch (Exception ex)
            {
                job.Complete(new ExecutionResult(
                    job.JobId, JobStatus.Failed, string.Empty, job.PlanHash,
                    DateTime.UtcNow, DateTime.UtcNow, true, null, null, null,
                    new[] { new AgentError(ErrorCodes.InternalError, ex.Message, false) }));
            }
        }

        private JsonValue HandleConfirm(string requestId, JsonValue payload)
        {
            string jobId = payload["jobId"].AsString(null);
            string action = payload["action"].AsString(null);
            string tokenValue = payload["token"].AsString(null);

            Job job = _jobQueue.Find(jobId);
            if (job == null)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.PlanConfirm,
                    new AgentError(ErrorCodes.JobNotFound, "Job not found: " + jobId, false));
            }

            ConfirmationToken token = _tokenStore.Get(jobId);
            if (token == null)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.PlanConfirm,
                    new AgentError(ErrorCodes.ConfirmationTokenInvalid, "No token issued for this job.", false));
            }

            if (action == "accept")
            {
                AgentError error = token.Validate(tokenValue);
                if (error != null)
                {
                    return PipeProtocol.Fail(requestId, ProtocolCatalog.PlanConfirm, error);
                }

                token.Accept();
                _dispatcher.Enqueue(new MainThreadWorkItem(
                    (app, doc) => ExecutePlan(app, doc, job, ProtocolCatalog.PlanCommit),
                    "Resume plan " + jobId));
            }
            else if (action == "reject")
            {
                token.Reject();
                job.Transition(JobStatus.Cancelled);
                _auditLog.Append("user", "plan.confirm", AuditSeverity.Security,
                    "Plan rejected: " + job.Plan.Description, null, job.PlanHash, jobId);
            }
            else
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.PlanConfirm,
                    new AgentError(ErrorCodes.InvalidArgument, "Action must be 'accept' or 'reject'.", true));
            }

            return PipeProtocol.Ok(requestId, ProtocolCatalog.PlanConfirm,
                JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["jobId"] = JsonValue.String(jobId),
                    ["status"] = JsonValue.String(ExecutionResult.StatusToWire(job.Status))
                }));
        }

        private JsonValue HandleJobStatus(string requestId, JsonValue payload)
        {
            string jobId = payload["jobId"].AsString(null);
            Job job = _jobQueue.Find(jobId);
            if (job == null)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.JobStatus,
                    new AgentError(ErrorCodes.JobNotFound, "Job not found: " + jobId, false));
            }

            return PipeProtocol.Ok(requestId, ProtocolCatalog.JobStatus, job.ToStatusJson());
        }

        private JsonValue HandleJobCancel(string requestId, JsonValue payload)
        {
            string jobId = payload["jobId"].AsString(null);
            Job job = _jobQueue.Find(jobId);
            if (job == null)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.JobCancel,
                    new AgentError(ErrorCodes.JobNotFound, "Job not found: " + jobId, false));
            }

            if (!job.IsCancellable)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.JobCancel,
                    new AgentError(ErrorCodes.JobNotCancellable, "Job is not cancellable in state " + job.Status, false));
            }

            job.Transition(JobStatus.Cancelled);
            _auditLog.Append("mcp", "job.cancel", AuditSeverity.Warning,
                "Job cancelled: " + jobId, null, null, jobId);

            return PipeProtocol.Ok(requestId, ProtocolCatalog.JobCancel,
                job.ToStatusJson());
        }

        private JsonValue HandleJobRollback(string requestId, JsonValue payload)
        {
            string jobId = payload["jobId"].AsString(null);
            Job job = _jobQueue.Find(jobId);
            if (job == null)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.JobRollback,
                    new AgentError(ErrorCodes.JobNotFound, "Job not found: " + jobId, false));
            }

            if (job.Status != JobStatus.Completed)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.JobRollback,
                    new AgentError(ErrorCodes.RollbackNotPossible,
                        "Only completed jobs can be rolled back.", false));
            }

            // Rollback must run on the main thread. We block until it completes because
            // the pipe request expects a synchronous verdict.
            var done = new System.Threading.AutoResetEvent(false);
            JsonValue[] errorHolder = new JsonValue[1];
            _dispatcher.Enqueue(new MainThreadWorkItem((app, doc) =>
            {
                try
                {
                    var executor = new PlanExecutor();
                    executor.Rollback(doc, job);
                }
                catch (AgentException ex)
                {
                    errorHolder[0] = new AgentError(ex.Code, ex.Message, ex.Recoverable).ToJson();
                }
                catch (Exception ex)
                {
                    errorHolder[0] = new AgentError(ErrorCodes.RollbackNotPossible, ex.Message, false).ToJson();
                }
                finally
                {
                    done.Set();
                }
            }, "Rollback " + jobId));

            done.WaitOne(TimeSpan.FromSeconds(30));

            if (errorHolder[0] != null && errorHolder[0].IsObject)
            {
                return PipeProtocol.Fail(requestId, ProtocolCatalog.JobRollback,
                    AgentError.FromJson(errorHolder[0]));
            }

            return PipeProtocol.Ok(requestId, ProtocolCatalog.JobRollback,
                job.ToStatusJson());
        }

        private JsonValue HandleAuditLog(string requestId, JsonValue payload)
        {
            string prefix = payload["actionPrefix"].AsString(null);
            int limit = payload["limit"].AsInt(500);

            var entries = _auditLog.Snapshot(prefix, limit);
            var list = new List<JsonValue>(entries.Count);
            foreach (AuditLogEntry entry in entries)
            {
                list.Add(entry.ToJson());
            }

            return PipeProtocol.Ok(requestId, ProtocolCatalog.AuditLog,
                JsonValue.Object(new Dictionary<string, JsonValue>(StringComparer.Ordinal)
                {
                    ["entries"] = JsonValue.Array(list),
                    ["count"] = JsonValue.Number(list.Count)
                }));
        }

        private JsonValue DispatchAndRun(string method, string requestId, JsonValue payload)
        {
            // Read operations must run on the main thread. The pipe handler is already on
            // a background thread, so we block here until the dispatcher work item finishes.
            var done = new System.Threading.AutoResetEvent(false);
            JsonValue[] resultHolder = new JsonValue[1];
            AgentException[] errorHolder = new AgentException[1];

            _dispatcher.Enqueue(new MainThreadWorkItem((app, doc) =>
            {
                try
                {
                    resultHolder[0] = ReadOperationRouter.Handle(app, doc, method, payload);
                }
                catch (AgentException ex)
                {
                    errorHolder[0] = ex;
                }
                catch (Exception ex)
                {
                    errorHolder[0] = new AgentException(ErrorCodes.InternalError, ex.Message, false);
                }
                finally
                {
                    done.Set();
                }
            }, "Read " + method));

            // Wait for the main thread to run the operation (30s cap).
            done.WaitOne(TimeSpan.FromSeconds(30));

            if (errorHolder[0] != null)
            {
                var ex = errorHolder[0];
                return PipeProtocol.Fail(requestId, method,
                    new AgentError(ex.Code, ex.Message, ex.Recoverable, ex.SuggestedAction));
            }

            return PipeProtocol.Ok(requestId, method, resultHolder[0] ?? JsonValue.EmptyObject());
        }
    }
}
