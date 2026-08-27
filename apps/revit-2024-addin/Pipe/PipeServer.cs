using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Contracts;

namespace AutodeskNativeAgent.Revit2024.Pipe
{
    /// <summary>
    /// A length-prefixed named-pipe server that accepts one client at a time. Frames are
    /// <c>[4-byte little-endian length][UTF-8 JSON]</c>, capped at
    /// <see cref="PipeProtocol.MaxMessageBytes"/> bytes. The server is single-client by
    /// design: the MCP server is its only peer.
    /// </summary>
    /// <remarks>
    /// All public members are safe to call from the Revit main thread; the listener runs on a
    /// background thread but dispatches to the <see cref="RequestHandler"/> delegate, which
    /// hosts must marshal onto their own synchronisation context (Revit's API is not
    /// thread-safe outside a transaction).
    /// </remarks>
    public sealed class PipeServer : IDisposable
    {
        /// <summary>Handles one request envelope and returns a response envelope.</summary>
        public delegate JsonValue RequestHandler(JsonValue request, string requestId);

        private readonly string _pipeName;
        private readonly RequestHandler _handler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _listenerThread;

        /// <summary>True while the listener is running.</summary>
        public bool IsRunning => _listenerThread != null && _listenerThread.IsAlive;

        /// <summary>Creates a server for the given pipe name.</summary>
        public PipeServer(string pipeName, RequestHandler handler)
        {
            if (string.IsNullOrEmpty(pipeName))
            {
                throw new ArgumentNullException(nameof(pipeName));
            }

            _pipeName = pipeName;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>Starts listening on a background thread.</summary>
        public void Start()
        {
            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                return;
            }

            Console.WriteLine("[REVIT] pipeName=" + _pipeName);
            Console.WriteLine("[REVIT] waiting");
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "AutodeskNativeAgent.PipeListener"
            };
            _listenerThread.Start();
        }

        /// <summary>Stops the listener.</summary>
        public void Stop()
        {
            _cts.Cancel();
            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                // The accept loop is interruptible via PipeStream disposal at shutdown.
                _listenerThread.Join(TimeSpan.FromSeconds(2));
            }
        }

        private void ListenLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        pipe.WaitForConnection();
                        if (_cts.IsCancellationRequested)
                        {
                            break;
                        }

                        HandleClient(pipe);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // A transient pipe failure should not kill the server; retry after a pause.
                    if (!_cts.IsCancellationRequested)
                    {
                        Thread.Sleep(200);
                    }
                }
            }
        }

        private void HandleClient(NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                Console.WriteLine("[REVIT] client connected");
                while (!_cts.IsCancellationRequested && pipe.IsConnected)
                {
                    JsonValue frame = ReadFrame(pipe);
                    if (frame == null)
                    {
                        break; // client closed the connection or sent garbage
                    }

                    string requestId = frame["requestId"].AsString(PipeProtocol.NewRequestId());
                    string method = frame["method"].AsString(null);

                    JsonValue response;
                    try
                    {
                        // Pass the FULL frame to the handler so it can read method + payload.
                        response = _handler(frame, requestId);
                        Console.WriteLine("[REVIT] request handled: " + (method ?? "<null>"));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[REVIT] request FAILED: " + (method ?? "<null>") + " - " + ex.Message);
                        response = PipeProtocol.Envelope(
                            PipeProtocol.TypeResponse,
                            requestId,
                            method,
                            null,
                            error: new AgentError(ErrorCodes.InternalError, "Runtime exception: " + ex.Message));
                    }

                    WriteFrame(pipe, JsonWriter.Write(response));
                    Console.WriteLine("[REVIT] response sent: " + (method ?? "<null>"));
                }

                Console.WriteLine("[REVIT] client disconnected");
            }
        }

        private static JsonValue ReadFrame(NamedPipeServerStream pipe)
        {
            byte[] header = ReadExactly(pipe, 4);
            if (header == null)
            {
                return null;
            }

            int length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > PipeProtocol.MaxMessageBytes)
            {
                return JsonValue.Null; // protocol violation; close the client
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
                return JsonValue.Null;
            }
        }

        private static byte[] ReadExactly(NamedPipeServerStream pipe, int count)
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

        private static void WriteFrame(NamedPipeServerStream pipe, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            if (body.Length > PipeProtocol.MaxMessageBytes)
            {
                throw new InvalidOperationException("Response exceeds the pipe message limit.");
            }

            byte[] length = BitConverter.GetBytes(body.Length);
            pipe.Write(length, 0, 4);
            pipe.Write(body, 0, body.Length);
            pipe.Flush();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }
    }
}
