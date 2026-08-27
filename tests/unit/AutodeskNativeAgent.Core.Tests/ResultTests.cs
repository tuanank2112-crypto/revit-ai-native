using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    public class ErrorAndResultTests
    {
        [Fact]
        public void AgentError_roundtrips_through_json()
        {
            var error = new AgentError(ErrorCodes.InvalidArgument, "bad", true, "Fix it.")
                .With("field", "height")
                .With("limit", 3);

            JsonValue json = error.ToJson();
            Assert.Equal(ErrorCodes.InvalidArgument, json["code"].AsString());
            Assert.True(json["recoverable"].AsBool());
            Assert.Equal("bad", json["message"].AsString());

            AgentError parsed = AgentError.FromJson(json);
            Assert.NotNull(parsed);
            Assert.Equal(ErrorCodes.InvalidArgument, parsed.Code);
            Assert.Equal("Fix it.", parsed.SuggestedAction);
            Assert.Equal("height", parsed.Details["field"].AsString());
        }

        [Fact]
        public void ExecutionResult_serializes_terminal_state()
        {
            var result = new ExecutionResult(
                "job-1",
                JobStatus.Completed,
                "fp-1",
                "hash-1",
                System.DateTime.UtcNow.AddSeconds(-5),
                System.DateTime.UtcNow,
                atomic: true,
                operations: new[]
                {
                    new OperationResult(
                        "op1",
                        "wall.create",
                        OperationOutcome.Completed,
                        created: new[]
                        {
                            new ElementSummary(123, "uid-123", "Walls", "Wall 1", "Generic 200"),
                        }),
                },
                assertions: new[]
                {
                    new AssertionResult("length", "$result.op1", JsonValue.Number(1000), JsonValue.Number(1000.5), 0.5, 1, true),
                });

            JsonValue json = result.ToJson();
            Assert.Equal("completed", json["status"].AsString());
            Assert.Equal(1, json["operations"].Count);
            Assert.Equal("wall.create", json["operations"][0]["operation"].AsString());
            Assert.Equal("uid-123", json["operations"][0]["createdElements"][0]["uniqueId"].AsString());
            Assert.Equal(1, json["assertions"].Count);
            Assert.True(json["assertions"][0]["passed"].AsBool());
            Assert.True(json["atomic"].AsBool());
        }

        [Fact]
        public void PreviewReport_serializes_summary_and_dry_run_flag()
        {
            var report = new PreviewReport(
                "hash",
                "fp",
                new[]
                {
                    new OperationPreview("op1", "wall.create", PreviewStatus.Ready, willCreate: 1),
                },
                new PreviewSummary(1, 0, 0, 1, requiresUserConfirmation: true));

            JsonValue json = report.ToJson();
            Assert.True(json["dryRun"].AsBool());
            Assert.Equal("ready", json["operations"][0]["status"].AsString());
            Assert.Equal(1, json["summary"]["willCreate"].AsInt());
            Assert.True(json["summary"]["requiresUserConfirmation"].AsBool());
        }
    }
}
