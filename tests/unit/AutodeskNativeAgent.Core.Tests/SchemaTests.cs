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

        // --- New Tier-1 schemas (2026-08-31) ---

        [Fact]
        public void Level_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("level.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"elevation\":3000,\"name\":\"L2\"}")));
            // missing required elevation
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"name\":\"L2\"}"))).ToLowerInvariant());
        }

        [Fact]
        public void Grid_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("grid.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":8000,\"y\":0},\"name\":\"1\"}")));
            // missing end
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"start\":{\"x\":0,\"y\":0}}"))).ToLowerInvariant());
        }

        [Fact]
        public void Family_load_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("family.load");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"path\":\"C:\\\\Families\\\\Door.rfa\",\"symbolName\":\"Door 900x2100\"}")));
            // missing path
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{}"))).ToLowerInvariant());
        }

        [Fact]
        public void Family_instance_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("family.instance.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"point\":{\"x\":1000,\"y\":2000,\"z\":0}," +
                "\"type\":{\"strategy\":\"project_default_or_fail\"}," +
                "\"level\":{\"strategy\":\"active_view_level\"}," +
                "\"structural\":\"column\",\"category\":\"column\"}")));
            // missing type
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"point\":{\"x\":1,\"y\":1}}"))).ToLowerInvariant());
        }

        [Fact]
        public void Column_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("column.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"point\":{\"x\":1000,\"y\":1000}," +
                "\"type\":{\"strategy\":\"project_default_or_fail\"}," +
                "\"level\":{\"strategy\":\"active_view_level\"}," +
                "\"structural\":\"column\"}")));
            // missing point
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"type\":{\"strategy\":\"project_default_or_fail\"}}"))).ToLowerInvariant());
        }

        [Fact]
        public void Beam_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("beam.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":4000,\"y\":0}," +
                "\"depth\":300,\"width\":200,\"baseOffset\":0," +
                "\"level\":{\"strategy\":\"active_view_level\"}," +
                "\"type\":{\"strategy\":\"project_default_or_fail\"}}")));
            // missing level
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":1,\"y\":0},\"type\":{\"strategy\":\"project_default_or_fail\"}}"))).ToLowerInvariant());
        }

        [Fact]
        public void Slab_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("slab.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"outline\":{\"points\":[{\"x\":0,\"y\":0},{\"x\":8000,\"y\":0},{\"x\":8000,\"y\":10000},{\"x\":0,\"y\":10000}]}," +
                "\"level\":{\"strategy\":\"exact_name\",\"name\":\"L2\"},\"name\":\"House_Slab_L2\"}")));
            // outline points < 3: minItems violation
            string errors = string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse(
                "{\"outline\":{\"points\":[{\"x\":0,\"y\":0},{\"x\":1,\"y\":1}]},\"level\":{\"strategy\":\"exact_name\",\"name\":\"L2\"}}")));
            Assert.Contains("fewer than 3", errors);
        }

        [Fact]
        public void Roof_create_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("roof.create");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"outline\":{\"points\":[{\"x\":-400,\"y\":-400},{\"x\":8400,\"y\":-400},{\"x\":8400,\"y\":10400},{\"x\":-400,\"y\":10400}]}," +
                "\"level\":{\"strategy\":\"exact_name\",\"name\":\"L2\"},\"overhang\":400,\"name\":\"House_Roof\"}")));
            // missing outline
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"level\":{\"strategy\":\"exact_name\",\"name\":\"L2\"}}"))).ToLowerInvariant());
        }

        [Fact]
        public void View_create_section_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("view.create_section");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"box\":{\"min\":{\"x\":2500,\"y\":4000,\"z\":-300},\"max\":{\"x\":5500,\"y\":6000,\"z\":7200}}," +
                "\"viewType\":\"section\",\"name\":\"House Section A\"}")));
            // missing box
            Assert.Contains("required", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse("{\"viewType\":\"section\"}"))).ToLowerInvariant());
        }

        [Fact]
        public void View_create_elevation_schema_validates()
        {
            JsonValue schema = SchemaCatalog.LoadOperationSchema("view.create_elevation");
            Assert.Empty(CollectValidationErrors(schema, JsonParser.Parse(
                "{\"box\":{\"min\":{\"x\":-1000,\"y\":-1000,\"z\":-1000},\"max\":{\"x\":12000,\"y\":12000,\"z\":5000}}," +
                "\"viewType\":\"elevation\",\"name\":\"E2E_Probe_Elevation\"}")));
            // wrong viewType rejected
            Assert.Contains("const", string.Join("; ", CollectValidationErrors(schema, JsonParser.Parse(
                "{\"box\":{\"min\":{\"x\":0,\"y\":0},\"max\":{\"x\":1,\"y\":1}},\"viewType\":\"section\"}"))).ToLowerInvariant());
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
