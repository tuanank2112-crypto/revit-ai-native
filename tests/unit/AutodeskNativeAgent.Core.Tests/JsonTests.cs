using System;
using System.Collections.Generic;
using AutodeskNativeAgent.Core.Json;
using Xunit;

namespace AutodeskNativeAgent.Core.Tests
{
    public class JsonParserTests
    {
        [Fact]
        public void Parse_roundtrips_compact_and_keeps_types()
        {
            const string text = "{\"a\":1,\"b\":2.5,\"c\":\"x\",\"d\":true,\"e\":null,\"f\":[1,2],\"g\":{\"h\":\"i\"}}";
            JsonValue json = JsonParser.Parse(text);

            Assert.Equal(JsonKind.Object, json.Kind);
            Assert.Equal(1d, json["a"].AsDouble());
            Assert.Equal(2.5d, json["b"].AsDouble());
            Assert.Equal("x", json["c"].AsString());
            Assert.True(json["d"].AsBool());
            Assert.True(json["e"].IsNull);
            Assert.Equal(2, json["f"].Count);
            Assert.Equal(2d, json["f"][1].AsDouble());
            Assert.Equal("i", json["g"]["h"].AsString());
        }

        [Fact]
        public void Parse_rejects_trailing_comma()
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse("{\"a\":1,}"));
            Assert.Throws<JsonException>(() => JsonParser.Parse("[1,2,]"));
        }

        [Fact]
        public void Parse_rejects_control_characters_in_strings()
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse("\"a\u0001b\""));
        }

        [Fact]
        public void Parse_rejects_leading_zero()
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse("01"));
        }

        [Fact]
        public void Parse_accepts_unicode_escapes_and_surrogates()
        {
            JsonValue json = JsonParser.Parse("\"\\u0041\\uD83D\\uDE00\"");
            Assert.Equal("A\uD83D\uDE00", json.AsString());
        }

        [Fact]
        public void Parse_rejects_unterminated_structures()
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse("{\"a\":1"));
            Assert.Throws<JsonException>(() => JsonParser.Parse("[1,2"));
            Assert.Throws<JsonException>(() => JsonParser.Parse("\"abc"));
        }

        [Fact]
        public void Parse_rejects_trailing_content()
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse("{} {}"));
        }

        [Fact]
        public void Parse_enforces_depth_limit()
        {
            var limits = new JsonLimits { MaxDepth = 3 };
            Assert.Throws<JsonException>(() => JsonParser.Parse("[[[[1]]]]", limits));
        }

        [Fact]
        public void Parse_enforces_max_length()
        {
            var limits = new JsonLimits { MaxLength = 4 };
            Assert.Throws<JsonException>(() => JsonParser.Parse("[1,2,3]", limits));
        }

        [Fact]
        public void TryParse_reports_error_without_throwing()
        {
            JsonValue value;
            string error;
            Assert.False(JsonParser.TryParse("{broken", out value, out error));
            Assert.Null(value);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Duplicate_keys_last_wins()
        {
            JsonValue json = JsonParser.Parse("{\"a\":1,\"a\":2}");
            Assert.Equal(2d, json["a"].AsDouble());
            Assert.Equal(1, json.Count);
        }
    }

    public class JsonWriterTests
    {
        [Fact]
        public void Write_compact_escapes_strings()
        {
            var json = JsonValue.Object(new Dictionary<string, JsonValue>
            {
                ["s"] = JsonValue.String("a\"b\\c\nd"),
            });
            string text = JsonWriter.Write(json);
            Assert.Equal("{\"s\":\"a\\\"b\\\\c\\nd\"}", text);
        }

        [Fact]
        public void Write_number_integral_has_no_fraction()
        {
            Assert.Equal("42", JsonWriter.Write(JsonValue.Number(42d)));
            Assert.Equal("42.5", JsonWriter.Write(JsonValue.Number(42.5d)));
        }

        [Fact]
        public void WriteCanonical_sorts_object_members()
        {
            var members = new Dictionary<string, JsonValue>
            {
                ["z"] = JsonValue.Number(1),
                ["a"] = JsonValue.Number(2),
                ["m"] = JsonValue.Object(new Dictionary<string, JsonValue>
                {
                    ["k2"] = JsonValue.Number(1),
                    ["k1"] = JsonValue.Number(2),
                }),
            };
            string text = JsonWriter.WriteCanonical(JsonValue.Object(members));
            Assert.Equal("{\"a\":2,\"m\":{\"k1\":2,\"k2\":1},\"z\":1}", text);
        }

        [Fact]
        public void WriteIndented_includes_newlines()
        {
            string text = JsonWriter.WriteIndented(JsonValue.Object(new Dictionary<string, JsonValue>
            {
                ["a"] = JsonValue.Number(1),
            }));
            Assert.Contains('\n', text);
        }
    }
}
