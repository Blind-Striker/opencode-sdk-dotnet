using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserUnionTests
{
    [Test]
    public async Task Parse_Should_Produce_Literal_Node_For_Single_Value_Enum()
    {
        var document = SpecFixture.ParseSchemas(
            """{ "OAuthKind": { "type": "string", "enum": ["oauth"] } }""");

        var literal = (LiteralNode)document.Schemas["OAuthKind"];
        await Assert.That(literal.Kind).IsEqualTo(LiteralKind.String);
        await Assert.That(literal.Value).IsEqualTo("oauth");
        await Assert.That(literal.Dialect).IsEqualTo(LiteralDialect.SingleValueEnum);
    }

    [Test]
    public async Task Parse_Should_Produce_Literal_Node_For_Const()
    {
        var document = SpecFixture.ParseSchemas(
            """{ "Marker": { "type": "string", "const": "text" } }""");

        var literal = (LiteralNode)document.Schemas["Marker"];
        await Assert.That(literal.Kind).IsEqualTo(LiteralKind.String);
        await Assert.That(literal.Value).IsEqualTo("text");
        await Assert.That(literal.Dialect).IsEqualTo(LiteralDialect.Const);
    }

    [Test]
    public async Task Parse_Should_Produce_Literal_Node_For_Boolean_Enum()
    {
        var document = SpecFixture.ParseSchemas(
            """{ "Healthy": { "type": "boolean", "enum": [true] } }""");

        var literal = (LiteralNode)document.Schemas["Healthy"];
        await Assert.That(literal.Kind).IsEqualTo(LiteralKind.Boolean);
        await Assert.That(literal.Value).IsEqualTo("true");
        await Assert.That(literal.Dialect).IsEqualTo(LiteralDialect.SingleValueEnum);
    }

    [Test]
    public async Task Parse_Should_Collect_Literal_Markers_On_Object_Nodes()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Event": {
                                                    "type": "object",
                                                    "properties": {
                                                      "type": { "type": "string", "enum": ["created"] },
                                                      "id": { "type": "string" },
                                                      "status": { "type": "string", "const": "ready" }
                                                    },
                                                    "required": ["type", "id"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        var marker = ((ObjectNode)document.Schemas["Event"]).LiteralMarkers.Single();
        await Assert.That(marker.PropertyName).IsEqualTo("type");
        await Assert.That(marker.Kind).IsEqualTo(LiteralKind.String);
        await Assert.That(marker.Value).IsEqualTo("created");
    }

    [Test]
    public async Task Parse_Should_Produce_Union_Node_For_AnyOf()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Part": {
                                                    "anyOf": [
                                                      { "$ref": "#/components/schemas/A" },
                                                      { "$ref": "#/components/schemas/B" }
                                                    ]
                                                  },
                                                  "A": {
                                                    "type": "object",
                                                    "properties": {
                                                      "type": { "type": "string", "enum": ["a"] }
                                                    },
                                                    "required": ["type"],
                                                    "additionalProperties": false
                                                  },
                                                  "B": {
                                                    "type": "object",
                                                    "properties": {
                                                      "type": { "type": "string", "enum": ["b"] }
                                                    },
                                                    "required": ["type"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        var union = (UnionNode)document.Schemas["Part"];
        await Assert.That(union.Keyword).IsEqualTo(UnionKeyword.AnyOf);
        await Assert.That(union.Branches.Count).IsEqualTo(2);
        await Assert.That(((RefNode)union.Branches[0]).Target).IsEqualTo("A");
        await Assert.That(((RefNode)union.Branches[1]).Target).IsEqualTo("B");
    }

    [Test]
    public async Task Parse_Should_Produce_Union_Node_For_OneOf()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Part": {
                                                    "oneOf": [
                                                      { "$ref": "#/components/schemas/A" },
                                                      { "$ref": "#/components/schemas/B" }
                                                    ]
                                                  },
                                                  "A": { "type": "string" },
                                                  "B": { "type": "number" }
                                                }
                                                """);

        var union = (UnionNode)document.Schemas["Part"];
        await Assert.That(union.Keyword).IsEqualTo(UnionKeyword.OneOf);
        await Assert.That(union.Branches.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_Should_Parse_Nested_Unions()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "ToolState": {
                                                    "anyOf": [
                                                      {
                                                        "type": "object",
                                                        "properties": {
                                                          "type": { "type": "string", "enum": ["running"] },
                                                          "state": {
                                                            "anyOf": [
                                                              {
                                                                "type": "object",
                                                                "properties": {
                                                                  "type": { "type": "string", "enum": ["started"] }
                                                                },
                                                                "required": ["type"],
                                                                "additionalProperties": false
                                                              },
                                                              {
                                                                "type": "object",
                                                                "properties": {
                                                                  "type": { "type": "string", "enum": ["stopped"] }
                                                                },
                                                                "required": ["type"],
                                                                "additionalProperties": false
                                                              }
                                                            ]
                                                          }
                                                        },
                                                        "required": ["type", "state"],
                                                        "additionalProperties": false
                                                      },
                                                      {
                                                        "type": "object",
                                                        "properties": {
                                                          "type": { "type": "string", "enum": ["idle"] }
                                                        },
                                                        "required": ["type"],
                                                        "additionalProperties": false
                                                      }
                                                    ]
                                                  }
                                                }
                                                """);

        var outer = (UnionNode)document.Schemas["ToolState"];
        var running = (ObjectNode)document.Schemas[((RefNode)outer.Branches[0]).Target];
        var state = (RefNode)running.Properties.Single(property => property.Name == "state").Schema;
        await Assert.That(state.Target).IsEqualTo("ToolState#/anyOf/type=running/properties/state");

        var inner = (UnionNode)document.Schemas[state.Target];
        await Assert.That(inner.Keyword).IsEqualTo(UnionKeyword.AnyOf);
        await Assert
            .That(((RefNode)inner.Branches[0]).Target)
            .IsEqualTo("ToolState#/anyOf/type=running/properties/state/anyOf/type=started");
        await Assert
            .That(((RefNode)inner.Branches[1]).Target)
            .IsEqualTo("ToolState#/anyOf/type=running/properties/state/anyOf/type=stopped");
    }

    [Test]
    public async Task Parse_Should_Promote_Union_Branches_With_Marker_Keys()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Evt": {
                                                    "anyOf": [
                                                      {
                                                        "type": "object",
                                                        "properties": {
                                                          "type": { "type": "string", "enum": ["created"] }
                                                        },
                                                        "required": ["type"],
                                                        "additionalProperties": false
                                                      },
                                                      {
                                                        "type": "object",
                                                        "properties": {
                                                          "type": { "type": "string", "enum": ["deleted"] }
                                                        },
                                                        "required": ["type"],
                                                        "additionalProperties": false
                                                      }
                                                    ]
                                                  }
                                                }
                                                """);

        var union = (UnionNode)document.Schemas["Evt"];
        await Assert.That(((RefNode)union.Branches[0]).Target).IsEqualTo("Evt#/anyOf/type=created");
        await Assert.That(((RefNode)union.Branches[1]).Target).IsEqualTo("Evt#/anyOf/type=deleted");
        await Assert.That(document.Schemas["Evt#/anyOf/type=created"]).IsTypeOf<ObjectNode>();
        await Assert.That(document.Schemas["Evt#/anyOf/type=deleted"]).IsTypeOf<ObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Wrap_Nullable_When_AnyOf_Has_Null_Branch()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Project": {
                                                    "anyOf": [
                                                      { "$ref": "#/components/schemas/Summary" },
                                                      { "type": "null" }
                                                    ]
                                                  },
                                                  "Summary": {
                                                    "type": "object",
                                                    "properties": {},
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        var nullable = (NullableNode)document.Schemas["Project"];
        await Assert.That(((RefNode)nullable.Inner).Target).IsEqualTo("Summary");
    }

    [Test]
    public async Task Parse_Should_Dedup_Duplicate_Refs_In_AnyOf()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Choice": {
                                                    "anyOf": [
                                                      { "$ref": "#/components/schemas/A" },
                                                      { "$ref": "#/components/schemas/B" },
                                                      { "$ref": "#/components/schemas/B" }
                                                    ]
                                                  },
                                                  "A": { "type": "string" },
                                                  "B": { "type": "boolean" }
                                                }
                                                """);

        var union = (UnionNode)document.Schemas["Choice"];
        await Assert.That(union.Branches.Count).IsEqualTo(2);
        await Assert.That(((RefNode)union.Branches[0]).Target).IsEqualTo("A");
        await Assert.That(((RefNode)union.Branches[1]).Target).IsEqualTo("B");
    }

    [Test]
    public async Task Parse_Should_Collapse_To_Plain_Ref_When_Dedup_Leaves_One_Branch()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Choice": {
                                                    "anyOf": [
                                                      { "$ref": "#/components/schemas/A" },
                                                      { "$ref": "#/components/schemas/A" }
                                                    ]
                                                  },
                                                  "A": { "type": "string" }
                                                }
                                                """);

        await Assert.That(document.Schemas["Choice"]).IsTypeOf<RefNode>();
        await Assert.That(((RefNode)document.Schemas["Choice"]).Target).IsEqualTo("A");
    }

    [Test]
    public async Task Parse_Should_Union_Primitive_And_Literal_Branches()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Timeout": {
                                                    "anyOf": [
                                                      { "type": "number" },
                                                      { "type": "boolean", "enum": [false] }
                                                    ]
                                                  }
                                                }
                                                """);

        var union = (UnionNode)document.Schemas["Timeout"];
        await Assert.That(((PrimitiveNode)union.Branches[0]).Kind).IsEqualTo(PrimitiveKind.Number);
        await Assert.That(((LiteralNode)union.Branches[1]).Kind).IsEqualTo(LiteralKind.Boolean);
        await Assert.That(((LiteralNode)union.Branches[1]).Value).IsEqualTo("false");
    }

    [Test]
    public async Task Parse_Should_Refuse_Multi_Value_Boolean_Enum()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas(
                """{ "Bad": { "type": "boolean", "enum": [true, false] } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("enum");
        await Assert.That(ex.Message).Contains("Bad");
    }
}
