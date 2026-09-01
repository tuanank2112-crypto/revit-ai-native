using System;
using System.Collections.Concurrent;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AutodeskNativeAgent.Revit2024.Execution
{
    /// <summary>
    /// A work item that must execute on the Revit main thread. The callback receives
    /// the active <see cref="Document"/> and the <see cref="UIApplication"/> context.
    /// </summary>
    public sealed class MainThreadWorkItem
    {
        /// <summary>Creates a work item.</summary>
        public MainThreadWorkItem(Action<UIApplication, Document> callback, string description)
        {
            Callback = callback ?? throw new ArgumentNullException(nameof(callback));
            Description = description ?? string.Empty;
            EnqueuedAtUtc = DateTime.UtcNow;
        }

        /// <summary>The callback to invoke on the main thread.</summary>
        public Action<UIApplication, Document> Callback { get; }

        /// <summary>Human-readable description for logs and the pending-work indicator.</summary>
        public string Description { get; }

        /// <summary>When the item was enqueued.</summary>
        public DateTime EnqueuedAtUtc { get; }
    }

    /// <summary>
    /// Thread-safe queue + ExternalEvent dispatcher. The MCP server's pipe listener
    /// enqueues work here; the ExternalEvent fires on the next Revit idle pulse and
    /// the handler dequeues and runs one item at a time on the main thread.
    /// </summary>
    /// <remarks>
    /// Revit's API is apartment-threaded: it can only be called from the thread that
    /// owns the Document. This dispatcher is the only bridge between the pipe's
    /// background listener and the Revit API.
    /// </remarks>
    public sealed class MainThreadDispatcher : IExternalEventHandler
    {
        private readonly BlockingCollection<MainThreadWorkItem> _queue =
            new BlockingCollection<MainThreadWorkItem>();
        private ExternalEvent _externalEvent;
        private readonly object _eventGate = new object();

        /// <summary>Creates the dispatcher.</summary>
        public MainThreadDispatcher()
        {
        }

        /// <summary>
        /// Creates the ExternalEvent if it does not already exist. Must be called from
        /// the Revit main thread (e.g. during <c>OnStartup</c>) — Revit throws
        /// "Attempting to create an ExternalEvent outside of a standard API execution"
        /// if it is created from a background (pipe listener) thread.
        /// </summary>
        public void EnsureCreated()
        {
            lock (_eventGate)
            {
                if (_externalEvent == null)
                {
                    _externalEvent = ExternalEvent.Create(this);
                }
            }
        }

        /// <summary>Raises the ExternalEvent so Revit calls <see cref="Execute"/> on the next idle.</summary>
        public void Raise()
        {
            EnsureCreated();
            _externalEvent.Raise();
        }

        /// <summary>Enqueues a work item and raises the ExternalEvent.</summary>
        public void Enqueue(MainThreadWorkItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _queue.TryAdd(item, TimeSpan.FromSeconds(2));
            Raise();
        }

        /// <summary>Number of items waiting in the queue.</summary>
        public int PendingCount => _queue.Count;

        /// <summary>True when there are pending items.</summary>
        public bool HasPendingWork => _queue.Count > 0;

        /// <summary>
        /// Called by Revit on the main thread when the ExternalEvent fires.
        /// Processes all queued items until the queue is empty.
        /// </summary>
        public void Execute(UIApplication app)
        {
            MainThreadWorkItem item;
            while (_queue.TryTake(out item, 0))
            {
                try
                {
                    Document doc = null;
                    try
                    {
                        doc = app.ActiveUIDocument?.Document;
                    }
                    catch
                    {
                        doc = null;
                    }

                    item.Callback(app, doc);
                }
                catch (Exception ex)
                {
                    // A failed work item must not stop the dispatcher from processing the
                    // rest of the queue. The callback itself wraps its own error handling;
                    // anything reaching here is a bug — log it instead of swallowing it.
                    System.Diagnostics.Trace.WriteLine(
                        "[MainThreadDispatcher] Work item failed: " + item.Description +
                        " — " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        /// <summary>The name shown in Revit's External Events list.</summary>
        public string GetName()
        {
            return "AutodeskNativeAgent.MainThreadDispatcher";
        }

        /// <summary>Disposes the underlying ExternalEvent.</summary>
        public void Dispose()
        {
            _queue.CompleteAdding();
            lock (_eventGate)
            {
                if (_externalEvent != null)
                {
                    _externalEvent.Dispose();
                    _externalEvent = null;
                }
            }
        }
    }
}
