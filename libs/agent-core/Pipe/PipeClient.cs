using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;

namespace AutodeskNativeAgent.Core.Pipe
{
    /// <summary>Message sizes and types shared with the Revit add-in's PipeServer.</summary>
    public static class PipeProtocol
    {
        /// <summary>Maximum accepted message size (4 MiB).</summary>
        public const int MaxMessageBytes = 4 * 1024 * 1024;

        /// <summary>Message types from pipe-envelope.schema.json.</summary>
        public const string TypeRequest = "request";
        public const string TypeHeartbeat = "heartbeat";
        public const string TypeResponse = "response";

        /// <summary>Generates a request id.</summary>
        public static string NewRequestId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        /// <summary>Builds a request envelope per pipe-envelope.schema.json.</summary>
        public static JsonValue Envelope(string type, string requestId, string method, JsonValue payload, JsonValue data = null, AgentError error = null, string correlationId = null)
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            {
                ["protocolVersion"] = JsonValue.String("1.0"),
                ["requestId"] = JsonValue.String(requestId ?? NewRequestId()),
                ["type"] = JsonValue.String(type),
                ["timestampUtc"] = JsonValue.String(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"))
            };

            if (!string.IsNullOrEmpty(correlationId))
            {
                members["correlationId"] = JsonValue.String(correlationId);
            }

            if (!string.IsNullOrEmpty(method))
            {
                members["method"] = JsonValue.String(method);
            }

            if (payload != null && !payload.IsNull)
            {
                members["payload"] = payload;
            }

            if (data != null && !data.IsNull)
            {
                members["data"] = data;
                members["success"] = JsonValue.Bool(true);
            }

            if (error != null)
            {
                members["error"] = error.ToJson();
                members["success"] = JsonValue.Bool(false);
            }

            return JsonValue.Object(members);
        }

        /// <summary>Builds a heartbeat envelope.</summary>
        public static JsonValue Heartbeat(string requestId)
        {
            return Envelope(TypeHeartbeat, requestId, "heartbeat", null);
        }
    }
    /// <summary>
    /// Named-pipe client used by the MCP server (or any non-Revit process) to talk to the
    /// Revit add-in. Sends a length-prefixed JSON envelope and waits for a response
    /// correlated by requestId. Supports timeouts, heartbeat, automatic reconnection,
    /// and cancellation.
    /// </summary>
    /// <remarks>
    /// This type lives in the shared core so that both the MCP server (net8.0) and
    /// integration tests can use it. It targets net48 as well, so it avoids
    /// async-over-sync anti-patterns and uses synchronous I/O on a background thread.
    /// </remarks>
    public sealed class PipeClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly TimeSpan _requestTimeout;
        private readonly TimeSpan _heartbeatInterval;
        private NamedPipeClientStream _stream;
        private Thread _heartbeatThread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _sendGate = new object();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonValue>> _pending =
            new ConcurrentDictionary<string, TaskCompletionSource<JsonValue>>(StringComparer.Ordinal);

        /// <summary>True when the client believes it is connected.</summary>
        public bool IsConnected => _stream != null && _stream.IsConnected;

        /// <summary>
        /// Raised once when the underlying pipe is lost (peer closed, add-in unloaded,
        /// or the stream faulted). Hosts use this to surface a health status and trigger
        /// reconnection. Fires at most once per connection loss.
        /// </summary>
        public event EventHandler ConnectionLost;

        private volatile bool _lossReported;

        /// <summary>Creates a client for the given pipe name.</summary>
        public PipeClient(
            string pipeName,
            TimeSpan? requestTimeout = null,
            TimeSpan? heartbeatInterval = null)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new ArgumentNullException(nameof(pipeName));
            }

            _pipeName = pipeName;
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
            _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(15);
        }

        /// <summary>Connects to the named pipe server. Throws when the server is not found.</summary>
        public void Connect(int timeoutMs = 5000)
        {
            lock (_sendGate)
            {
                if (IsConnected)
                {
                    return;
                }

                _stream = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                _stream.Connect(timeoutMs);
                _stream.ReadMode = PipeTransmissionMode.Byte;

                // Start reader + heartbeat on background threads.
                var readerThread = new Thread(ReadLoop)
                {
                    IsBackground = true,
                    Name = "AutodeskNativeAgent.PipeClient.Reader"
                };
                readerThread.Start();

                _heartbeatThread = new Thread(HeartbeatLoop)
                {
                    IsBackground = true,
                    Name = "AutodeskNativeAgent.PipeClient.Heartbeat"
                };
                _heartbeatThread.Start();
            }
        }

        /// <summary>Sends a request and waits for the correlated response.</summary>
        public JsonValue Request(string method, JsonValue payload, string correlationId = null)
        {
            if (!IsConnected)
            {
                throw new AgentErrorResult(
                    ErrorCodes.RevitNotConnected,
                    "The Revit add-in is not connected.",
                    true,
                    "Ensure Revit is running and the add-in is loaded.");
            }

            string requestId = PipeProtocol.NewRequestId();
            JsonValue envelope = PipeProtocol.Envelope(
                PipeProtocol.TypeRequest,
                requestId,
                method,
                payload,
                null,
                null,
                correlationId);

            var tcs = new TaskCompletionSource<JsonValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = tcs;

            lock (_sendGate)
            {
                WriteFrame(_stream, JsonWriter.Write(envelope));
            }

            // Use Task.Wait with a timeout (net48-safe: no Task.WaitAsync).
            if (!tcs.Task.Wait((int)_requestTimeout.TotalMilliseconds))
            {
                TaskCompletionSource<JsonValue> removed;
                _pending.TryRemove(requestId, out removed);
                throw new AgentErrorResult(
                    ErrorCodes.RequestTimeout,
                    "Request '" + method + "' timed out after " + _requestTimeout.TotalSeconds + "s.",
                    true,
                    "Check whether Revit is busy with a modal dialog.");
            }

            JsonValue response = tcs.Task.Result;
            if (!response["success"].AsBool(true))
            {
                AgentError error = AgentError.FromJson(response["error"]);
                if (error != null)
                {
                    throw new AgentErrorResult(error.Code, error.Message, error.Recoverable, error.SuggestedAction);
                }

                throw new AgentErrorResult(ErrorCodes.InternalError, "The add-in returned an unknown error.");
            }

            return response["data"];
        }

        /// <summary>Disconnects and disposes the client.</summary>
        public void Disconnect()
        {
            _cts.Cancel();
            lock (_sendGate)
            {
                if (_stream != null)
                {
                    try { _stream.Dispose(); } catch { }
                    _stream = null;
                }
            }

            // Fail all pending requests.
            foreach (var pair in _pending)
            {
                pair.Value.TrySetException(new AgentErrorResult(
                    ErrorCodes.PipeError,
                    "The pipe connection was closed."));
            }

            _pending.Clear();
        }

        private void ReadLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                JsonValue frame;
                try
                {
                    // Snapshot the stream without holding the send gate. Reads and
                    // writes on a full-duplex NamedPipeClientStream are independent;
                    // holding _sendGate during a blocking read would deadlock
                    // Request()/HeartbeatLoop(), which need the same gate to write.
                    NamedPipeClientStream stream;
                    lock (_sendGate)
                    {
                        if (_stream == null || !_stream.IsConnected)
                        {
                            break;
                        }

                        stream = _stream;
                    }

                    frame = ReadFrame(stream);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Disconnect() disposes the stream mid-read (ObjectDisposedException)
                    // or the peer closed the connection. Treat both as end-of-connection.
                    break;
                }

                if (frame == null)
                {
                    break;
                }

                string requestId = frame["requestId"].AsString(null);
                if (string.IsNullOrEmpty(requestId))
                {
                    continue;
                }

                // Heartbeat responses and notifications do not have a pending request.
                TaskCompletionSource<JsonValue> tcs;
                if (_pending.TryRemove(requestId, out tcs))
                {
                    tcs.SetResult(frame);
                }
            }

            // Fail all pending requests on disconnect.
            foreach (var pair in _pending)
            {
                pair.Value.TrySetException(new AgentErrorResult(
                    ErrorCodes.PipeError,
                    "The pipe connection was lost."));
            }

            _pending.Clear();
            RaiseConnectionLostOnce();
        }

        private void RaiseConnectionLostOnce()
        {
            if (_lossReported)
            {
                return;
            }

            _lossReported = true;
            EventHandler handler = ConnectionLost;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // Host callbacks must never tear down the reader thread.
                }
            }
        }

        private void HeartbeatLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_cts.Token.WaitHandle.WaitOne(_heartbeatInterval))
                {
                    break;
                }

                try
                {
                    lock (_sendGate)
                    {
                        if (_stream == null || !_stream.IsConnected)
                        {
                            break;
                        }

                        string hbId = PipeProtocol.NewRequestId();
                        WriteFrame(_stream, JsonWriter.Write(PipeProtocol.Heartbeat(hbId)));
                    }
                }
                catch (Exception)
                {
                    // Heartbeat failure is silent; the read loop will fail pending requests.
                    break;
                }
            }
        }

        private static void WriteFrame(PipeStream pipe, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            if (body.Length > PipeProtocol.MaxMessageBytes)
            {
                throw new InvalidOperationException("Request exceeds the pipe message limit.");
            }

            byte[] length = BitConverter.GetBytes(body.Length);
            pipe.Write(length, 0, 4);
            pipe.Write(body, 0, body.Length);
            pipe.Flush();
        }

        private static JsonValue ReadFrame(PipeStream pipe)
        {
            byte[] header = ReadExactly(pipe, 4);
            if (header == null)
            {
                return null;
            }

            int length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > PipeProtocol.MaxMessageBytes)
            {
                return null;
            }

            byte[] body = ReadExactly(pipe, length);
            if (body == null)
            {
                return null;
            }

            string text;
            try
            {
                text = Encoding.UTF8.GetString(body);
                return JsonParser.Parse(text);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[] ReadExactly(PipeStream pipe, int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = pipe.Read(buffer, offset, count - offset);
                if (read == 0)
                {
                    return null;
                }

                offset += read;
            }

            return buffer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Disconnect();
            _cts.Dispose();
        }
    }
}
