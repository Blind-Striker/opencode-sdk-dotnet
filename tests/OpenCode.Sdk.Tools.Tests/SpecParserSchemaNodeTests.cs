using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserSchemaNodeTests
{
    [Test]
    public async Task Parse_Should_Produce_Primitive_Nodes_For_Scalar_Types()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "A": { "type": "string" },
                                                  "B": { "type": "number" },
                                                  "C": { "type": "integer" },
                                                  "D": { "type": "boolean" }
                                                }
                                                """);

        await Assert.That(((PrimitiveNode)document.Schemas["A"]).Kind).IsEqualTo(PrimitiveKind.String);
        await Assert.That(((PrimitiveNode)document.Schemas["B"]).Kind).IsEqualTo(PrimitiveKind.Number);
        await Assert.That(((PrimitiveNode)document.Schemas["C"]).Kind).IsEqualTo(PrimitiveKind.Integer);
        await Assert.That(((PrimitiveNode)document.Schemas["D"]).Kind).IsEqualTo(PrimitiveKind.Boolean);
    }

    [Test]
    public async Task Parse_Should_Record_Format_On_Primitive_Nodes()
    {
        var document = SpecFixture.ParseSchemas("""{ "Bin": { "type": "string", "format": "binary" } }""");

        await Assert.That(((PrimitiveNode)document.Schemas["Bin"]).Format).IsEqualTo("binary");
    }

    [Test]
    public async Task Parse_Should_Ignore_Validation_Keywords()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Id": { "type": "string", "pattern": "^ses" },
                                                  "Seq": { "type": "integer", "minimum": 0, "exclusiveMinimum": 0, "maximum": 10 },
                                                  "Batch": { "type": "array", "minItems": 1, "items": { "type": "string" } }
                                                }
                                                """);

        await Assert.That(((PrimitiveNode)document.Schemas["Id"]).Kind).IsEqualTo(PrimitiveKind.String);
        await Assert.That(((PrimitiveNode)document.Schemas["Seq"]).Kind).IsEqualTo(PrimitiveKind.Integer);
        await Assert.That(((PrimitiveNode)((ArrayNode)document.Schemas["Batch"]).Item).Kind).IsEqualTo(PrimitiveKind.String);
    }

    [Test]
    public async Task Parse_Should_Keep_Dotted_Schema_Names_Verbatim()
    {
        var document = SpecFixture.ParseSchemas("""{ "session.status": { "type": "string" } }""");

        await Assert.That(document.Schemas.ContainsKey("session.status")).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Record_Description_On_Nodes()
    {
        var document = SpecFixture.ParseSchemas("""{ "Doc": { "type": "string", "description": "documented" } }""");

        await Assert.That(document.Schemas["Doc"].Description).IsEqualTo("documented");
    }

    [Test]
    public async Task Parse_Should_Produce_Enum_Node_For_Multi_Value_String_Enum()
    {
        var document = SpecFixture.ParseSchemas("""{ "Kind": { "type": "string", "enum": ["file", "directory"] } }""");

        var values = ((EnumNode)document.Schemas["Kind"]).Values;
        await Assert.That(values.Count).IsEqualTo(2);
        await Assert.That(values[0]).IsEqualTo("file");
        await Assert.That(values[1]).IsEqualTo("directory");
    }

    [Test]
    public async Task Parse_Should_Produce_Array_Node_With_Item()
    {
        var document = SpecFixture.ParseSchemas("""{ "Names": { "type": "array", "items": { "type": "string" } } }""");

        var item = ((ArrayNode)document.Schemas["Names"]).Item;
        await Assert.That(((PrimitiveNode)item).Kind).IsEqualTo(PrimitiveKind.String);
    }

    [Test]
    public async Task Parse_Should_Produce_Ref_Node_For_Component_Ref()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Target": { "type": "string" },
                                                  "Alias": { "$ref": "#/components/schemas/Target" }
                                                }
                                                """);

        await Assert.That(((RefNode)document.Schemas["Alias"]).Target).IsEqualTo("Target");
    }

    [Test]
    public async Task Parse_Should_Promote_Inline_Enum_Under_Array_Items()
    {
        var document = SpecFixture.ParseSchemas("""
                                                {
                                                  "Levels": { "type": "array", "items": { "type": "string", "enum": ["low", "high"] } }
                                                }
                                                """);

        await Assert.That(((RefNode)((ArrayNode)document.Schemas["Levels"]).Item).Target).IsEqualTo("Levels#/items");

        var values = ((EnumNode)document.Schemas["Levels#/items"]).Values;
        await Assert.That(values.Count).IsEqualTo(2);
        await Assert.That(values[0]).IsEqualTo("low");
        await Assert.That(values[1]).IsEqualTo("high");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Schema_Uses_AllOf()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "allOf": [ { "type": "string" } ] } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("allOf");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Schema_Uses_Discriminator()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "type": "object", "discriminator": { "propertyName": "type" } } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("discriminator");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Type_Is_An_Array()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "type": ["string", "null"] } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("type array");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Keyword_Is_Unknown()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "type": "string", "x-custom": true } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("x-custom");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Ref_Points_Outside_Component_Schemas()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "$ref": "#/components/responses/X" } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("#/components/responses/X");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Ref_Target_Is_Missing()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "$ref": "#/components/schemas/Ghost" } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("Ghost");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Array_Has_No_Items()
    {
        var ex = await Assert
            .That(() => SpecFixture
                .ParseSchemas("""{ "Bad": { "type": "array" } }"""))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("items");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Parse_Should_Batch_Multiple_Errors()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParseSchemas("""
                                                 {
                                                   "BadA": { "allOf": [] },
                                                   "BadB": { "type": "array" }
                                                 }
                                                 """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("BadA");
        await Assert.That(ex.Message).Contains("BadB");
    }
}
