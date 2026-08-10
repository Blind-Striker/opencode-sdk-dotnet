using OpenCode.Sdk.Tools.Generator.Parsing;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserObjectNodeTests
{
    [Test]
    public async Task Parse_Should_Produce_Object_Node_With_Properties_And_Required_Set()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Session": {
                                                    "type": "object",
                                                    "properties": { "id": { "type": "string" }, "title": { "type": "string" } },
                                                    "required": ["id"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        var node = (ObjectNode)document.Schemas["Session"];
        await Assert.That(node.Properties.Select(p => p.Name)).IsEquivalentTo(["id", "title"]);
        await Assert.That(node.Properties.Single(p => p.Name == "id").IsRequired).IsTrue();
        await Assert.That(node.Properties.Single(p => p.Name == "title").IsRequired).IsFalse();
        await Assert.That(node.AdditionalProperties).IsEqualTo(AdditionalPropertiesKind.Forbidden);
    }

    [Test]
    public async Task Parse_Should_Promote_Inline_Object_Property()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Parent": {
                                                    "type": "object",
                                                    "properties": {
                                                      "child": {
                                                        "type": "object",
                                                        "properties": { "x": { "type": "string" } },
                                                        "additionalProperties": false
                                                      }
                                                    },
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        var parent = (ObjectNode)document.Schemas["Parent"];
        var child = (RefNode)parent.Properties.Single(property => property.Name == "child").Schema;
        await Assert.That(child.Target).IsEqualTo("Parent#/properties/child");
        await Assert.That(document.Schemas["Parent#/properties/child"]).IsTypeOf<ObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Classify_Missing_Additional_Properties_As_Open()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Open": {
                                                    "type": "object",
                                                    "properties": { "id": { "type": "string" } }
                                                  }
                                                }
                                                """);

        var node = (ObjectNode)document.Schemas["Open"];
        await Assert.That(node.AdditionalProperties).IsEqualTo(AdditionalPropertiesKind.Open);
        await Assert.That(node.AdditionalPropertiesSchema).IsNull();
    }

    [Test]
    public async Task Parse_Should_Keep_Property_Schema_For_Hybrid_Objects()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Hybrid": {
                                                    "type": "object",
                                                    "properties": { "fixed": { "type": "boolean" } },
                                                    "additionalProperties": { "type": "string" }
                                                  }
                                                }
                                                """);

        var node = (ObjectNode)document.Schemas["Hybrid"];
        await Assert.That(node.AdditionalProperties).IsEqualTo(AdditionalPropertiesKind.Schema);
        await Assert.That(((PrimitiveNode)node.AdditionalPropertiesSchema!).Kind).IsEqualTo(PrimitiveKind.String);
        await Assert.That(((PrimitiveNode)node.Properties.Single().Schema).Kind).IsEqualTo(PrimitiveKind.Boolean);
    }

    [Test]
    public async Task Parse_Should_Produce_Dictionary_Node_When_Only_Additional_Properties_Schema()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Env": { "type": "object", "additionalProperties": { "type": "string" } }
                                                }
                                                """);

        var node = (DictionaryNode)document.Schemas["Env"];
        await Assert.That(((PrimitiveNode)node.Value).Kind).IsEqualTo(PrimitiveKind.String);
    }

    [Test]
    public async Task Parse_Should_Produce_Dictionary_Node_For_Pattern_Properties()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Active": {
                                                    "type": "object",
                                                    "patternProperties": { "^ses": { "$ref": "#/components/schemas/Target" } }
                                                  },
                                                  "Target": { "type": "object", "properties": {}, "additionalProperties": false }
                                                }
                                                """);

        var node = (DictionaryNode)document.Schemas["Active"];
        await Assert.That(((RefNode)node.Value).Target).IsEqualTo("Target");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Pattern_Properties_Has_Multiple_Patterns()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Bad": {
                                                     "type": "object",
                                                     "patternProperties": {
                                                       "^first": { "type": "string" },
                                                       "^second": { "type": "string" }
                                                     }
                                                   }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("patternProperties");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Pattern_Properties_Combined_With_Properties()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Bad": {
                                                     "type": "object",
                                                     "properties": {},
                                                     "patternProperties": { "^first": { "type": "string" } }
                                                   }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("patternProperties");
    }

    [Test]
    public async Task Parse_Should_Produce_Free_Form_Node_For_Bare_Object()
    {
        var document = SpecFixture.ParseSchemas("""{ "Meta": { "type": "object" } }""");

        await Assert.That(document.Schemas["Meta"]).IsTypeOf<FreeFormObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Produce_Empty_Object_Node_When_Properties_Is_Empty()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Unit": { "type": "object", "properties": {}, "additionalProperties": false }
                                                }
                                                """);

        var node = (ObjectNode)document.Schemas["Unit"];
        await Assert.That(node.Properties).IsEmpty();
        await Assert.That(node.AdditionalProperties).IsEqualTo(AdditionalPropertiesKind.Forbidden);
    }

    [Test]
    public async Task Parse_Should_Treat_Keyword_Named_Properties_As_Plain_Names()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Evt": {
                                                    "type": "object",
                                                    "properties": {
                                                      "type": { "type": "string" },
                                                      "properties": { "type": "object", "properties": {}, "additionalProperties": false },
                                                      "required": { "type": "boolean" }
                                                    },
                                                    "required": ["type", "properties"],
                                                    "additionalProperties": false
                                                  }
                                                }
                                                """);

        var node = (ObjectNode)document.Schemas["Evt"];
        await Assert.That(node.Properties.Select(property => property.Name)).IsEquivalentTo(["type", "properties", "required"]);
        await Assert.That(node.Properties.Single(property => property.Name == "type").IsRequired).IsTrue();
        await Assert.That(node.Properties.Single(property => property.Name == "properties").IsRequired).IsTrue();
        await Assert.That(node.Properties.Single(property => property.Name == "required").IsRequired).IsFalse();
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Additional_Properties_Is_True()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Bad": { "type": "object", "properties": {}, "additionalProperties": true }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("additionalProperties");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Required_Names_Missing_Property()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "Bad": {
                                                     "type": "object",
                                                     "properties": {},
                                                     "required": ["ghost"],
                                                     "additionalProperties": false
                                                   }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("ghost");
    }
}
