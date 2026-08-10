using OpenCode.Sdk.Tools.Generator.Parsing;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserQuirkNodeTests
{
    [Test]
    public async Task Parse_Should_Classify_Special_Value_Number()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "TimeUsed": {
                                                    "anyOf": [
                                                      { "type": "number" },
                                                      { "type": "string", "enum": ["NaN"] },
                                                      { "type": "string", "enum": ["Infinity"] },
                                                      { "type": "string", "enum": ["-Infinity"] },
                                                      { "type": "string", "enum": ["Infinity", "-Infinity", "NaN"] }
                                                    ]
                                                  }
                                                }
                                                """);

        await Assert.That(document.Schemas["TimeUsed"]).IsTypeOf<SpecialNumberNode>();
    }

    [Test]
    public async Task Parse_Should_Not_Classify_Special_Value_Number_When_Extra_Branch_Present()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "TimeUsed": {
                                                    "anyOf": [
                                                      { "type": "number" },
                                                      { "type": "string", "enum": ["NaN"] },
                                                      { "type": "string" }
                                                    ]
                                                  }
                                                }
                                                """);

        await Assert.That(document.Schemas["TimeUsed"]).IsTypeOf<UnionNode>();
    }

    [Test]
    public async Task Parse_Should_Produce_Tuple_Node_For_Prefix_Items()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Plugin": {
                                                    "type": "array",
                                                    "prefixItems": [
                                                      { "type": "string" },
                                                      { "type": "object" }
                                                    ],
                                                    "maxItems": 2,
                                                    "minItems": 2
                                                  }
                                                }
                                                """);

        var node = (TupleNode)document.Schemas["Plugin"];
        await Assert.That(node.Items.Count).IsEqualTo(2);
        await Assert.That(node.Items[0]).IsTypeOf<PrimitiveNode>();
        await Assert.That(((PrimitiveNode)node.Items[0]).Kind).IsEqualTo(PrimitiveKind.String);
        await Assert.That(node.Items[1]).IsTypeOf<FreeFormObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Tuple_Arity_Conflicts_With_Min_Max()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Bad": {
                                                     "type": "array",
                                                     "prefixItems": [
                                                       { "type": "string" },
                                                       { "type": "object" }
                                                     ],
                                                     "maxItems": 3
                                                   }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("maxItems");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Items_And_Prefix_Items_Coexist()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Bad": {
                                                     "type": "array",
                                                     "items": { "type": "string" },
                                                     "prefixItems": [ { "type": "string" } ]
                                                   }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("prefixItems");
    }

    [Test]
    public async Task Parse_Should_Produce_Json_String_Node_For_Content_Schema()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Stream": {
                                                    "type": "string",
                                                    "contentMediaType": "application/json",
                                                    "contentSchema": { "$ref": "#/components/schemas/Evt" }
                                                  },
                                                  "Evt": { "type": "string" }
                                                }
                                                """);

        var node = (JsonStringNode)document.Schemas["Stream"];
        await Assert.That(((RefNode)node.Inner).Target).IsEqualTo("Evt");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Content_Media_Type_Is_Not_Json()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Stream": {
                                                     "type": "string",
                                                     "contentMediaType": "text/plain",
                                                     "contentSchema": { "type": "string" }
                                                   }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("application/json");
    }

    [Test]
    public async Task Parse_Should_Detect_Effect_Tag_Error_Style()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "BadRequest": {
                                                    "type": "object",
                                                    "properties": {
                                                      "_tag": { "type": "string", "enum": ["BadRequest"] },
                                                      "name": { "type": "string", "enum": ["NamedError"] },
                                                      "data": { "type": "object" }
                                                    },
                                                    "required": ["_tag", "name", "data"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        await Assert.That(((ObjectNode)document.Schemas["BadRequest"]).ErrorStyle).IsEqualTo(ErrorStyle.EffectTag);
    }

    [Test]
    public async Task Parse_Should_Detect_Name_Data_Error_Style()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "MoveSessionError": {
                                                    "type": "object",
                                                    "properties": {
                                                      "name": {
                                                        "type": "string",
                                                        "enum": ["MoveSessionError"]
                                                      },
                                                      "data": {
                                                        "type": "object",
                                                        "properties": {
                                                          "message": { "type": "string" }
                                                        },
                                                        "required": ["message"],
                                                        "additionalProperties": false
                                                      }
                                                    },
                                                    "required": ["name", "data"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        await Assert.That(((ObjectNode)document.Schemas["MoveSessionError"]).ErrorStyle).IsEqualTo(ErrorStyle.NameData);
    }

    [Test]
    public async Task Parse_Should_Leave_Error_Style_None_For_Plain_Objects()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "MoveSessionError": {
                                                    "type": "object",
                                                    "properties": {
                                                      "message": { "type": "string" }
                                                    },
                                                    "required": ["message"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        await Assert.That(((ObjectNode)document.Schemas["MoveSessionError"]).ErrorStyle).IsEqualTo(ErrorStyle.None);
    }
}
