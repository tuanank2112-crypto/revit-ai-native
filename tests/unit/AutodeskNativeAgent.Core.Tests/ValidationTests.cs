using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Execution;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Policy;
using AutodeskNativeAgent.Core.Validation;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    public class PlanValidatorTests
    {
        private static (AgentPlan, CommandRegistry) Build(string opsJson)
        {
            AgentPlan plan = AgentPlan.FromJson(JsonParser.Parse(opsJson), out AgentError error);
            Assert.Null(error);
            return (plan, CommandRegistry.CreateDefault());
        }

        [Fact]
        public void Valid_plan_passes()
        {
            var (plan, registry) = Build(
                "{" +
                "  \"schemaVersion\":\"1.0\"," +
                "  \"requestId\":\"r\"," +
                "  \"description\":\"d\"," +
                "  \"document\":{\"strategy\":\"active_document\"}," +
                "  \"units\":\"mm\"," +
                "  \"coordinateSystem\":\"internal\"," +
                "  \"executionMode\":\"preview\"," +
                "  \"operations\":[" +
                "    {\"id\":\"op1\",\"op\":\"wall.create\",\"args\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1000,\"y\":0},\"height\":3000,\"level\":{\"strategy\":\"exact_name\",\"name\":\"Level 1\"},\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"Curtain Wall\"}}," +
                "    \"assertions\":[{\"kind\":\"length\",\"target\":\"$result.op1\",\"equals\":1000,\"unit\":\"mm\",\"tolerance\":1}]}" +
                "  ]," +
                "  \"safety\":{\"requireUserConfirmation\":false,\"createBackupBeforeCommit\":false,\"maximumElementsAffected\":100,\"rollbackOnWarning\":false,\"rollbackOnValidationFailure\":true}" +
                "}");

            PlanValidationResult result = PlanValidator.Validate(plan, registry);
            Assert.True(result.Valid, string.Join("; ", result.Errors));
        }

        [Fact]
        public void Duplicate_operation_id_rejected()
        {
            var (plan, registry) = Build(
                "{\"schemaVersion\":\"1.0\",\"requestId\":\"r\",\"description\":\"d\"," +
                "\"document\":{\"strategy\":\"active_document\"},\"units\":\"mm\"," +
                "\"coordinateSystem\":\"internal\",\"executionMode\":\"preview\"," +
                "\"operations\":[" +
                "  {\"id\":\"op1\",\"op\":\"wall.create\",\"args\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1,\"y\":0},\"height\":1,\"level\":{\"strategy\":\"exact_name\",\"name\":\"L\"},\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"T\"}}}," +
                "  {\"id\":\"op1\",\"op\":\"wall.create\",\"args\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1,\"y\":0},\"height\":1,\"level\":{\"strategy\":\"exact_name\",\"name\":\"L\"},\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"T\"}}}" +
                "]," +
                "\"safety\":{\"requireUserConfirmation\":false,\"createBackupBeforeCommit\":false,\"maximumElementsAffected\":100,\"rollbackOnWarning\":false,\"rollbackOnValidationFailure\":false}}");

            PlanValidationResult result = PlanValidator.Validate(plan, registry);
            Assert.False(result.Valid);
            Assert.Contains(result.Errors, e => e.Code == ErrorCodes.DuplicateOperationId);
        }

        [Fact]
        public void Unknown_operation_rejected()
        {
            var (plan, registry) = BuildPlan(
                "{\"schemaVersion\":\"1.0\",\"requestId\":\"r\",\"description\":\"d\"," +
                "\"document\":{\"strategy\":\"active_document\"},\"units\":\"mm\"," +
                "\"coordinateSystem\":\"internal\",\"executionMode\":\"preview\"," +
                "\"operations\":[{\"id\":\"op1\",\"op\":\"nuke.everything\",\"args\":{}}]," +
                "\"safety\":{\"requireUserConfirmation\":false,\"createBackupBeforeCommit\":false,\"maximumElementsAffected\":100,\"rollbackOnWarning\":false,\"rollbackOnValidationFailure\":false}}");

            PlanValidationResult result = PlanValidator.Validate(plan, registry);
            Assert.False(result.Valid);
            Assert.Contains(result.Errors, e => e.Code == ErrorCodes.UnknownOperation);
        }

        [Fact]
        public void Dependency_cycle_rejected()
        {
            var (plan, registry) = BuildPlan(
                "{\"schemaVersion\":\"1.0\",\"requestId\":\"r\",\"description\":\"d\"," +
                "\"document\":{\"strategy\":\"active_document\"},\"units\":\"mm\"," +
                "\"coordinateSystem\":\"internal\",\"executionMode\":\"preview\"," +
                "\"operations\":[" +
                "  {\"id\":\"a\",\"op\":\"element.delete\",\"dependsOn\":[\"b\"],\"args\":{\"target\":{\"elementId\":1}}}," +
                "  {\"id\":\"b\",\"op\":\"element.delete\",\"dependsOn\":[\"a\"],\"args\":{\"target\":{\"elementId\":2}}}" +
                "]," +
                "\"safety\":{\"requireUserConfirmation\":false,\"createBackupBeforeCommit\":false,\"maximumElementsAffected\":100,\"rollbackOnWarning\":false,\"rollbackOnValidationFailure\":false}}");

            PlanValidationResult result = PlanValidator.Validate(plan, registry);
            Assert.False(result.Valid);
            Assert.Contains(result.Errors, e => e.Code == ErrorCodes.DependencyCycle);
        }

        [Fact]
        public void Per_operation_argument_schema_enforced()
        {
            // wall.create requires start/end/height/level/type — missing end must fail.
            var (plan, registry) = BuildPlan(
                "{\"schemaVersion\":\"1.0\",\"requestId\":\"r\",\"description\":\"d\"," +
                "\"document\":{\"strategy\":\"active_document\"},\"units\":\"mm\"," +
                "\"coordinateSystem\":\"internal\",\"executionMode\":\"preview\"," +
                "\"operations\":[{\"id\":\"op1\",\"op\":\"wall.create\",\"args\":{\"start\":{\"x\":0,\"y\":0},\"height\":1,\"level\":{\"strategy\":\"exact_name\",\"name\":\"L\"},\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"T\"}}}]," +
                "\"safety\":{\"requireUserConfirmation\":false,\"createBackupBeforeCommit\":false,\"maximumElementsAffected\":100,\"rollbackOnWarning\":false,\"rollbackOnValidationFailure\":false}}");

            PlanValidationResult result = PlanValidator.Validate(plan, registry);
            Assert.False(result.Valid);
            Assert.Contains(result.Errors, e => e.Code == ErrorCodes.InvalidArgument);
        }

        [Fact]
        public void Affected_element_ceiling_enforced()
        {
            var (plan, registry) = BuildPlan(
                "{\"schemaVersion\":\"1.0\",\"requestId\":\"r\",\"description\":\"d\"," +
                "\"document\":{\"strategy\":\"active_document\"},\"units\":\"mm\"," +
                "\"coordinateSystem\":\"internal\",\"executionMode\":\"preview\"," +
                "\"operations\":[" +
                "  {\"id\":\"op1\",\"op\":\"wall.create\",\"args\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1,\"y\":0},\"height\":1,\"level\":{\"strategy\":\"exact_name\",\"name\":\"L\"},\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"T\"}}}" +
                "]," +
                "\"safety\":{\"requireUserConfirmation\":false,\"createBackupBeforeCommit\":false,\"maximumElementsAffected\":0,\"rollbackOnWarning\":false,\"rollbackOnValidationFailure\":false}}");

            PlanValidationResult result = PlanValidator.Validate(plan, registry);
            Assert.False(result.Valid);
            Assert.Contains(result.Errors, e => e.Code == ErrorCodes.AffectedElementLimitExceeded);
        }

        private static (AgentPlan, CommandRegistry) BuildPlan(string json)
        {
            AgentPlan plan = AgentPlan.FromJson(JsonParser.Parse(json), out AgentError error);
            Assert.Null(error);
            return (plan, CommandRegistry.CreateDefault());
        }
    }

    public class PlanHasherTests
    {
        [Fact]
        public void Hash_is_stable_across_member_order()
        {
            string jsonA = "{\"a\":1,\"b\":{\"x\":[1,2]},\"c\":\"v\"}";
            string jsonB = "{\"c\":\"v\",\"b\":{\"x\":[1,2]},\"a\":1}";
            Assert.Equal(
                PlanHasher.HashJson(JsonParser.Parse(jsonA)),
                PlanHasher.HashJson(JsonParser.Parse(jsonB)));
        }

        [Fact]
        public void Hash_changes_when_value_changes()
        {
            string jsonA = "{\"a\":1}";
            string jsonB = "{\"a\":2}";
            Assert.NotEqual(
                PlanHasher.HashJson(JsonParser.Parse(jsonA)),
                PlanHasher.HashJson(JsonParser.Parse(jsonB)));
        }

        [Fact]
        public void Hash_is_64_hex_chars()
        {
            string hash = PlanHasher.HashJson(JsonParser.Parse("{}"));
            Assert.Equal(64, hash.Length);
            Assert.Matches("^[0-9a-f]{64}$", hash);
        }
    }

    public class ResultReferenceResolverTests
    {
        [Fact]
        public void Resolves_top_level_result()
        {
            var results = new Dictionary<string, JsonValue>
            {
                ["op1"] = JsonParser.Parse("{\"uniqueId\":\"abc\"}"),
            };
            var resolver = new ResultReferenceResolver(results);
            JsonValue resolved;
            string error;
            Assert.True(resolver.TryResolve("$result.op1.uniqueId", out resolved, out error));
            Assert.Equal("abc", resolved.AsString());
        }

        [Fact]
        public void Resolves_array_index()
        {
            var results = new Dictionary<string, JsonValue>
            {
                ["op1"] = JsonParser.Parse("{\"createdElements\":[{\"uniqueId\":\"x\"}]}"),
            };
            var resolver = new ResultReferenceResolver(results);
            JsonValue resolved;
            string error;
            Assert.True(resolver.TryResolve("$result.op1.createdElements[0].uniqueId", out resolved, out error));
            Assert.Equal("x", resolved.AsString());
        }

        [Fact]
        public void Missing_operation_reported()
        {
            var resolver = new ResultReferenceResolver(new Dictionary<string, JsonValue>());
            JsonValue resolved;
            string error;
            Assert.False(resolver.TryResolve("$result.missing.id", out resolved, out error));
            Assert.Contains("missing", error);
        }

        [Fact]
        public void Non_result_reference_is_not_an_error()
        {
            var resolver = new ResultReferenceResolver(new Dictionary<string, JsonValue>());
            JsonValue resolved;
            string error;
            Assert.False(resolver.TryResolve("abc123", out resolved, out error));
            Assert.Null(error);
        }
    }

    public class ProjectPolicyTests
    {
        [Fact]
        public void Parses_policy_and_defaults_conservatively()
        {
            ProjectPolicy policy = ProjectPolicy.FromJson(
                JsonParser.Parse(
                    "{\"policyVersion\":\"1.0\",\"defaultExternalUnit\":\"mm\",\"allowHardDefaults\":false," +
                    "\"defaults\":{\"wallHeightMm\":3000}," +
                    "\"tolerances\":{\"lengthMm\":2}," +
                    "\"safety\":{\"alwaysPreviewDelete\":true,\"alwaysPreviewMoreThanElements\":15}}"));

            Assert.Equal(ExternalUnit.Mm, policy.DefaultExternalUnit);
            Assert.Equal(3000d, policy.FindDefault("wallHeightMm").Value);
            Assert.Equal(2d, policy.LengthToleranceMm);
            Assert.Equal(15, policy.AlwaysPreviewMoreThanElements);
        }

        [Fact]
        public void Malformed_policy_falls_back_to_defaults()
        {
            ProjectPolicy policy = ProjectPolicy.FromJson(JsonParser.Parse("{\"notAPolicy\":true}"));
            Assert.Equal(ExternalUnit.Mm, policy.DefaultExternalUnit);
            Assert.False(policy.AllowHardDefaults);
            Assert.Equal(20, policy.AlwaysPreviewMoreThanElements);
        }
    }
}
