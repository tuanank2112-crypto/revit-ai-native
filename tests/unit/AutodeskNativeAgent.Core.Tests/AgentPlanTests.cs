using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Validation;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    public class AgentPlanTests
    {
        internal static JsonValue BuildPlanJson()
        {
            return JsonParser.Parse(
                "{" +
                "  \"schemaVersion\":\"1.0\"," +
                "  \"requestId\":\"req-1\"," +
                "  \"description\":\"Test plan\"," +
                "  \"document\":{\"strategy\":\"active_document\"}," +
                "  \"units\":\"mm\"," +
                "  \"coordinateSystem\":\"internal\"," +
                "  \"executionMode\":\"preview\"," +
                "  \"operations\":[" +
                "    {\"id\":\"op1\",\"op\":\"wall.create\",\"args\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1000,\"y\":0},\"height\":3000,\"level\":{\"strategy\":\"exact_name\",\"name\":\"Level 1\"},\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"Generic - 200mm\"}}}" +
                "  ]," +
                "  \"safety\":{" +
                "    \"requireUserConfirmation\":true," +
                "    \"createBackupBeforeCommit\":true," +
                "    \"maximumElementsAffected\":100," +
                "    \"rollbackOnWarning\":true," +
                "    \"rollbackOnValidationFailure\":true" +
                "  }" +
                "}");
        }

        [Fact]
        public void FromJson_parses_and_roundtrips()
        {
            JsonValue json = BuildPlanJson();
            AgentPlan plan = AgentPlan.FromJson(json, out AgentError error);

            Assert.Null(error);
            Assert.NotNull(plan);
            Assert.Equal("req-1", plan.RequestId);
            Assert.Equal(ExternalUnit.Mm, plan.Units);
            Assert.Equal(ExecutionMode.Preview, plan.ExecutionMode);
            Assert.Single(plan.Operations);
            Assert.True(plan.Safety.CreateBackupBeforeCommit);

            JsonValue roundtrip = plan.ToJson();
            Assert.Equal("mm", roundtrip["units"].AsString());
            Assert.Equal("preview", roundtrip["executionMode"].AsString());
            Assert.Equal("1.0", roundtrip["schemaVersion"].AsString());
        }

        [Fact]
        public void FromJson_rejects_missing_units()
        {
            JsonValue json = BuildPlanJson();
            json = json.WithReplacedMember("units", JsonValue.Null);
            AgentPlan plan = AgentPlan.FromJson(json, out AgentError error);
            Assert.Null(plan);
            Assert.NotNull(error);
            Assert.Equal(ErrorCodes.UnitAmbiguous, error.Code);
        }

        [Fact]
        public void FromJson_rejects_unknown_unit()
        {
            JsonValue json = BuildPlanJson();
            json = json.WithReplacedMember("units", JsonValue.String("furlong"));
            AgentPlan plan = AgentPlan.FromJson(json, out AgentError error);
            Assert.Null(plan);
            Assert.Equal(ErrorCodes.UnitUnsupported, error.Code);
        }

        [Fact]
        public void FromJson_rejects_wrong_schema_version()
        {
            JsonValue json = BuildPlanJson();
            json = json.WithReplacedMember("schemaVersion", JsonValue.String("2.0"));
            AgentPlan plan = AgentPlan.FromJson(json, out AgentError error);
            Assert.Null(plan);
            Assert.Equal(ErrorCodes.SchemaValidationFailed, error.Code);
        }

        [Fact]
        public void FromJson_rejects_empty_operations()
        {
            JsonValue json = BuildPlanJson();
            json = json.WithReplacedMember("operations", JsonValue.EmptyArray());
            AgentPlan plan = AgentPlan.FromJson(json, out AgentError error);
            Assert.Null(plan);
            Assert.Equal(ErrorCodes.SchemaValidationFailed, error.Code);
        }
    }

    internal static class JsonTestExtensions
    {
        internal static JsonValue WithReplacedMember(this JsonValue json, string name, JsonValue value)
        {
            var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var member in json.Members)
            {
                members[member.Key] = member.Value;
            }

            members[name] = value;
            return JsonValue.Object(members);
        }
    }

    public class JobStateMachineTests
    {
        private static Execution.Job CreateJob()
        {
            AgentError error;
            AgentPlan plan = AgentPlan.FromJson(AgentPlanTests.BuildPlanJson(), out error);
            Assert.Null(error);
            return new Execution.Job("job-1", plan, "hash-1", "test");
        }

        [Fact]
        public void TryCancel_atomically_marks_queued_job_cancelled()
        {
            Execution.Job job = CreateJob();

            Assert.True(job.TryCancel());
            Assert.Equal(JobStatus.Cancelled, job.Status);
            Assert.True(job.CancellationRequested);
            Assert.False(job.IsCancellable);
            Assert.False(job.TryCancel());
        }

        [Fact]
        public void TryCancel_rejects_terminal_job_without_mutating_state()
        {
            Execution.Job job = CreateJob();
            job.Transition(JobStatus.WaitingForRevit);
            job.Transition(JobStatus.Validating);
            job.Transition(JobStatus.Executing);
            job.Transition(JobStatus.Verifying);
            job.Transition(JobStatus.Completed);

            Assert.False(job.TryCancel());
            Assert.Equal(JobStatus.Completed, job.Status);
            Assert.False(job.CancellationRequested);
        }

        [Fact]
        public void Status_snapshot_exposes_cancellation_request()
        {
            Execution.Job job = CreateJob();

            Assert.False(job.ToStatusJson()["cancellationRequested"].AsBool());
            job.TryCancel();
            Assert.True(job.ToStatusJson()["cancellationRequested"].AsBool());
            Assert.Equal("cancelled", job.ToStatusJson()["status"].AsString());
        }

        [Fact]
        public void Status_snapshot_exposes_progress_update()
        {
            Execution.Job job = CreateJob();

            job.UpdateProgress("executing", 2, 3, "Creating Revit elements.");
            JsonValue progress = job.ToStatusJson()["progress"];

            Assert.Equal("executing", progress["phase"].AsString());
            Assert.Equal(2, progress["completed"].AsInt());
            Assert.Equal(3, progress["total"].AsInt());
            Assert.Equal("Creating Revit elements.", progress["message"].AsString());
        }
    }

    public class UnitConversionTests
    {
        [Theory]
        [InlineData("mm", 1d, 304.8d)]
        [InlineData("cm", 1d, 30.48d)]
        [InlineData("m", 1d, 0.3048d)]
        [InlineData("inch", 1d, 12d)]
        public void FromFeet_lengths_match_physical_values(string unitText, double value, double feet)
        {
            ExternalUnit unit;
            Assert.True(UnitNames.TryParseLength(unitText, out unit));
            Assert.Equal(feet, UnitNames.FromFeet(value, unit), 9);
        }

        [Fact]
        public void MmToFeet_is_inverse_of_FeetToMm()
        {
            Assert.Equal(304.8d, UnitNames.FeetToMm(UnitNames.MmToFeet(304.8d)), 9);
        }

        [Fact]
        public void ToWire_roundtrips_all_units()
        {
            foreach (ExternalUnit unit in new[] { ExternalUnit.Mm, ExternalUnit.Cm, ExternalUnit.Meter, ExternalUnit.Inch, ExternalUnit.Foot })
            {
                ExternalUnit parsed;
                Assert.True(UnitNames.TryParseLength(UnitNames.ToWire(unit), out parsed));
                Assert.Equal(unit, parsed);
            }
        }
    }
}
