using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Pipe;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    public class PipeIntegrationTests
    {
        [Fact]
        public async Task PipeClient_roundtrips_request_and_correlates_response()
        {
            string pipeName = "ana-test-" + Guid.NewGuid().ToString("N");
            Task server = RunSingleResponseServer(pipeName);
            using (var client = new PipeClient(pipeName, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1)))
            {
                client.Connect(2000);
                JsonValue data = client.Request("test.echo", JsonValue.Object(new Dictionary<string, JsonValue>
                {
                    ["message"] = JsonValue.String("xin chao")
                }));
                Assert.Equal("xin chao", data["echo"].AsString());
            }
            await server;
        }

        [Fact]
        public void Envelope_and_heartbeat_match_protocol_contract()
        {
            JsonValue envelope = PipeProtocol.Envelope(PipeProtocol.TypeRequest, "request-1", "document.get_info", JsonValue.EmptyObject(), correlationId: "correlation-1");
            Assert.Equal("1.0", envelope["protocolVersion"].AsString());
            Assert.Equal("request-1", envelope["requestId"].AsString());
            Assert.Equal("request", envelope["type"].AsString());
            Assert.Equal("document.get_info", envelope["method"].AsString());
            Assert.Equal("correlation-1", envelope["correlationId"].AsString());
            Assert.False(string.IsNullOrEmpty(envelope["timestampUtc"].AsString()));
            JsonValue heartbeat = PipeProtocol.Heartbeat("heartbeat-1");
            Assert.Equal("heartbeat", heartbeat["type"].AsString());
            Assert.Equal("heartbeat", heartbeat["method"].AsString());
            Assert.Equal("heartbeat-1", heartbeat["requestId"].AsString());
        }

        [Fact]
        public void Error_envelope_has_unsuccessful_structured_error()
        {
            var error = new AgentError(ErrorCodes.InternalError, "test failure", true, suggestedAction: "retry");
            JsonValue envelope = PipeProtocol.Envelope(PipeProtocol.TypeResponse, "request-2", "test.fail", null, error: error);
            Assert.False(envelope["success"].AsBool(true));
            Assert.Equal(ErrorCodes.InternalError, envelope["error"]["code"].AsString());
            Assert.Equal("test failure", envelope["error"]["message"].AsString());
        }

        private static async Task RunSingleResponseServer(string pipeName)
        {
            using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                await server.WaitForConnectionAsync();
                JsonValue request = await ReadFrameAsync(server);
                JsonValue response = PipeProtocol.Envelope(PipeProtocol.TypeResponse, request["requestId"].AsString(), request["method"].AsString(), null,
                    data: JsonValue.Object(new Dictionary<string, JsonValue> { ["echo"] = JsonValue.String(request["payload"]["message"].AsString()) }));
                await WriteFrameAsync(server, response);
            }
        }

        private static async Task<JsonValue> ReadFrameAsync(PipeStream pipe)
        {
            byte[] header = await ReadExactlyAsync(pipe, 4);
            int length = BitConverter.ToInt32(header, 0);
            Assert.InRange(length, 1, PipeProtocol.MaxMessageBytes);
            byte[] body = await ReadExactlyAsync(pipe, length);
            return JsonParser.Parse(Encoding.UTF8.GetString(body));
        }

        private static async Task WriteFrameAsync(PipeStream pipe, JsonValue value)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonWriter.Write(value));
            byte[] header = BitConverter.GetBytes(body.Length);
            await pipe.WriteAsync(header, 0, header.Length);
            await pipe.WriteAsync(body, 0, body.Length);
            await pipe.FlushAsync();
        }

        private static async Task<byte[]> ReadExactlyAsync(PipeStream pipe, int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await pipe.ReadAsync(buffer, offset, count - offset);
                if (read == 0) throw new InvalidOperationException("Pipe closed before the frame was complete.");
                offset += read;
            }
            return buffer;
        }
    }
}
