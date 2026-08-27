using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Execution;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Revit2024.Execution;
using AutodeskNativeAgent.Revit2024.Pipe;

namespace AutodeskNativeAgent.Revit2024
{
    /// <summary>
    /// Revit 2024 add-in entry point. Wires the named-pipe server to the main-thread
    /// dispatcher, starts the listener, and registers the audit log.
    /// </summary>
    /// <remarks>
    /// The add-in is discovered through the .addin manifest. On startup it creates the
    /// pipe server, the dispatcher, the job queue, the audit log, and the confirmation
    /// token store. On shutdown it disposes them in reverse order.
    /// </remarks>
    public class AgentAddInApplication : IExternalApplication
    {
        private PipeServer _pipeServer;
        private MainThreadDispatcher _dispatcher;
        private JobQueue _jobQueue;
        private AuditLog _auditLog;
        private ConfirmationTokenStore _tokenStore;
        private AgentRequestRouter _router;

        /// <summary>The pipe server. Null before startup or after shutdown.</summary>
        public PipeServer PipeServer => _pipeServer;

        /// <summary>The dispatcher.</summary>
        public MainThreadDispatcher Dispatcher => _dispatcher;

        /// <summary>The job queue.</summary>
        public JobQueue JobQueue => _jobQueue;

        /// <summary>The audit log.</summary>
        public AuditLog AuditLog => _auditLog;

        /// <summary>The confirmation token store.</summary>
        public ConfirmationTokenStore TokenStore => _tokenStore;

        /// <summary>Called by Revit on startup.</summary>
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                _dispatcher = new MainThreadDispatcher();
                // ExternalEvent must be created inside a standard API execution
                // (main thread). OnStartup qualifies; the pipe listener thread does not.
                _dispatcher.EnsureCreated();
                _jobQueue = new JobQueue();
                _auditLog = new AuditLog(maxEntries: 5000);
                // Persist the audit trail to disk (JSON Lines, append-only) so it
                // survives Revit shutdown. Best-effort: a failed disk write never
                // blocks the runtime (see AuditLog.AttachFile).
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutodeskNativeAgent", "logs");
                _auditLog.AttachFile(System.IO.Path.Combine(logDir, "audit.jsonl"));
                _tokenStore = new ConfirmationTokenStore();

                // Pipe name is user-scoped so two users on one machine don't collide.
                string pipeName = PipeProtocol.UserScopedPipeName();

                _router = new AgentRequestRouter(application, _dispatcher, _jobQueue, _auditLog, _tokenStore);

                _pipeServer = new PipeServer(pipeName, _router.HandleRequest);
                _pipeServer.Start();

                _auditLog.Append("system", "addin.startup", AuditSeverity.Info,
                    "Autodesk Native Agent add-in started. Pipe: " + pipeName + "; audit file: " + System.IO.Path.Combine(logDir, "audit.jsonl"));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // If we can't start the pipe, we still let Revit load — the user can fix the
                // issue and reload the add-in. We log the failure to a local file as a last resort.
                try
                {
                    string crashDir = System.IO.Path.GetDirectoryName(GetCrashLogPath());
                    if (!System.IO.Directory.Exists(crashDir))
                    {
                        System.IO.Directory.CreateDirectory(crashDir);
                    }

                    System.IO.File.AppendAllText(
                        GetCrashLogPath(),
                        DateTime.UtcNow.ToString("o") + " " + ex + Environment.NewLine);
                }
                catch
                {
                    // last-ditch; nothing more we can do
                }

                return Result.Failed;
            }
        }

        /// <summary>Called by Revit on shutdown.</summary>
        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (_auditLog != null)
                {
                    _auditLog.Append("system", "addin.shutdown", AuditSeverity.Info,
                        "Autodesk Native Agent add-in shutting down.");
                }

                if (_pipeServer != null)
                {
                    _pipeServer.Dispose();
                }

                if (_dispatcher != null)
                {
                    _dispatcher.Dispose();
                }

                if (_jobQueue != null)
                {
                    _jobQueue.CompleteAdding();
                }

                if (_auditLog != null)
                {
                    _auditLog.Close();
                }

                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        private static string GetCrashLogPath()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(dir, "AutodeskNativeAgent", "addin-crash.log");
        }
    }
}
