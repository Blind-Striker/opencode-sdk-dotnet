using System.IO.Abstractions.TestingHelpers;
using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserTests
{
    [Test]
    public async Task Parse_Should_Return_Empty_Document_When_Spec_Has_No_Operations_Or_Schemas()
    {
        var document = SpecFixture.ParseSchemas("{}");

        await Assert.That(document.OpenApiVersion).IsEqualTo("3.1.0");
        await Assert.That(document.Operations).IsEmpty();
        await Assert.That(document.Schemas).IsEmpty();
    }

    [Test]
    public async Task Parse_Should_Ignore_Security_And_Tags_Sections()
    {
        var document = SpecFixture.Parse("""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {} },
          "security": [],
          "tags": [ { "name": "global", "description": "Global server routes." } ]
        }
        """);

        await Assert.That(document.OpenApiVersion).IsEqualTo("3.1.0");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_OpenApi_Version_Is_Not_3_1()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("""
        {
          "openapi": "3.0.3",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {} }
        }
        """)).Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("3.0.3");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Top_Level_Key_Is_Unknown()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {} },
          "webhooks": {}
        }
        """)).Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("webhooks");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Components_Member_Is_Not_Schemas()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("""
        {
          "openapi": "3.1.0",
          "info": { "title": "fixture", "version": "0.0.0" },
          "paths": {},
          "components": { "schemas": {}, "responses": {} }
        }
        """)).Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("responses");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Spec_File_Is_Missing()
    {
        var parser = new SpecParser(new MockFileSystem());

        var ex = await Assert.That(() => parser.Parse("spec/openapi.json"))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("spec/openapi.json");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Json_Is_Malformed()
    {
        var ex = await Assert.That(() => SpecFixture.Parse("{ not json"))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("JSON");
    }

    [Test]
    public async Task SpecParser_Should_Guard_Null_FileSystem()
    {
        await Assert.That(() => new SpecParser(null!)).Throws<ArgumentNullException>();
    }
}
