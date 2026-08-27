using System.Collections.Generic;
using AutodeskNativeAgent.Core.Contracts;
using AutodeskNativeAgent.Core.Json;
using AutodeskNativeAgent.Core.Validation;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    public class SchemaCatalogTests
    {
        [Fact]
        public void Wall_create_schema_inlines_its_own_defs()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("wall.create");
            List<string> errors = CollectValidationErrors(schema, JsonParser.Parse(
                "{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1,\"y\":0},\"height\":1," +
                "\"level\":{\"strategy\":\"exact_name\",\"name\":\"L\"}," +
                "\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"T\"}}"));
            Assert.Empty(errors);
        }

        [Fact]
        public void Wall_create_schema_rejects_missing_required()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("wall.create");
            List<string> errors = CollectValidationErrors(schema, JsonParser.Parse(
                "{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1,\"y\":0},\"height\":1}"));
            Assert.Contains("required", string.Join("; ", errors).ToLowerInvariant());
        }

        [Fact]
        public void Door_insert_resolves_cross_file_defs()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("door.insert");
            List<string> errors = CollectValidationErrors(schema, JsonParser.Parse(
                "{\"host\":{\"operationResult\":\"$result.wall1\"}," +
                "\"location\":{\"strategy\":\"wall_midpoint\"}," +
                "\"level\":{\"strategy\":\"exact_name\",\"name\":\"L\"}," +
                "\"type\":{\"strategy\":\"exact_name\",\"typeName\":\"Door 900x2100\"}}"));
            Assert.Empty(errors);
        }

        [Fact]
        public void Parameter_set_schema_one_of_parameter_strategies()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("parameter.set");
            // Both builtIn and name given -> oneOf violation.
            List<string> errors = CollectValidationErrors(schema, JsonParser.Parse(
                "{\"target\":{\"uniqueId\":\"abc\"}," +
                "\"parameter\":{\"builtIn\":\"ALL_MODEL_MARK\",\"name\":\"Mark\"}," +
                "\"value\":{\"kind\":\"string\",\"string\":\"A1\"}}"));
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Element_reference_schema_any_of_satisfied()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("element.delete");
            List<string> errors = CollectValidationErrors(schema, JsonParser.Parse("{\"target\":{\"uniqueId\":\"abc\"}}"));
            Assert.Empty(errors);
        }

        private static List<string> CollectValidationErrors(JsonValue schema, JsonValue args)
        {
            return JsonSchemaValidator.Validate(args, schema);
        }
    }

    public class JsonSchemaValidatorTests
    {
        [Fact]
        public void Enforces_additional_properties_false()
        {
            JsonValue schema = JsonParser.Parse(
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"a\":{\"type\":\"number\"}}}");
            var errors = JsonSchemaValidator.Validate(JsonParser.Parse("{\"a\":1,\"b\":2}"), schema);
            Assert.Contains(errors, e => e.Contains("b"));
        }

        [Fact]
        public void Enforces_const_and_enum()
        {
            JsonValue schema = JsonParser.Parse(
                "{\"type\":\"object\",\"properties\":{\"v\":{\"enum\":[\"x\",\"y\"]},\"s\":{\"const\":\"1.0\"}}}");
            var errors = JsonSchemaValidator.Validate(JsonParser.Parse("{\"v\":\"z\",\"s\":\"2.0\"}"), schema);
            Assert.Equal(2, errors.Count);
        }

        [Fact]
        public void Enforces_pattern()
        {
            JsonValue schema = JsonParser.Parse("{\"type\":\"string\",\"pattern\":\"^[a-z]+$\"}");
            Assert.Empty(JsonSchemaValidator.Validate(JsonValue.String("abc"), schema));
            Assert.NotEmpty(JsonSchemaValidator.Validate(JsonValue.String("ABC"), schema));
        }

        [Fact]
        public void Enforces_integer_type()
        {
            JsonValue schema = JsonParser.Parse("{\"type\":\"integer\"}");
            Assert.Empty(JsonSchemaValidator.Validate(JsonValue.Number(3), schema));
            Assert.NotEmpty(JsonSchemaValidator.Validate(JsonValue.Number(3.5), schema));
        }

        [Fact]
        public void Enforces_numeric_bounds()
        {
            JsonValue schema = JsonParser.Parse("{\"type\":\"number\",\"minimum\":1,\"maximum\":10}");
            Assert.NotEmpty(JsonSchemaValidator.Validate(JsonValue.Number(0.5), schema));
            Assert.Empty(JsonSchemaValidator.Validate(JsonValue.Number(5), schema));
        }
    }
}
