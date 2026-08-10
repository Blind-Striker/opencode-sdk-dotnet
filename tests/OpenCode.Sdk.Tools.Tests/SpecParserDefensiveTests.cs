using OpenCode.Sdk.Tools.Generator.Parsing;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserDefensiveTests
{
    private const string ReadOnlyFixture = """
                                           {
                                             "openapi": "3.1.0",
                                             "info": { "title": "fixture", "version": "0.0.0" },
                                             "paths": {
                                               "/test": {
                                                 "get": {
                                                   "operationId": "v2.test.get",
                                                   "responses": { "204": { "description": "ok" } }
                                                 }
                                               }
                                             },
                                             "components": {
                                               "schemas": {
                                                 "Item": {
                                                   "type": "object",
                                                   "properties": { "id": { "type": "string" } },
                                                   "required": ["id"],
                                                   "additionalProperties": false
                                                 },
                                                 "Choice": {
                                                   "anyOf": [
                                                     { "$ref": "#/components/schemas/Item" },
                                                     { "type": "string" }
                                                   ]
                                                 }
                                               }
                                             }
                                           }
                                           """;

    [Test]
    public async Task Parse_Should_Refuse_When_Document_Root_Is_Not_Object()
    {
        await AssertRefusal("[]", "document: root must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Components_Is_Not_Object()
    {
        await AssertRefusal(
            BuildDocument("[]", "{}"),
            "document: 'components' must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Component_Schemas_Is_Not_Object()
    {
        await AssertRefusal(
            BuildDocument("""{ "schemas": [] }""", "{}"),
            "document: 'components.schemas' must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Paths_Is_Not_Object()
    {
        await AssertRefusal(
            BuildDocument("""{ "schemas": {} }""", "[]"),
            "document: 'paths' must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Path_Item_Is_Not_Object()
    {
        await AssertRefusal(
            SpecFixtureDocumentForPaths("""{ "/test": [] }"""),
            "path '/test': path item must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Operation_Is_Not_Object()
    {
        await AssertRefusal(
            SpecFixtureDocumentForPaths("""{ "/test": { "get": [] } }"""),
            "path '/test' method 'get': operation must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Parameters_Is_Not_Array()
    {
        await AssertRefusal(
            BuildOperationDocument("""
                                   {
                                     "operationId": "v2.test.create",
                                     "parameters": {},
                                     "responses": { "204": { "description": "ok" } }
                                   }
                                   """),
            "operation 'v2.test.create': parameters must be an array");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Parameter_Is_Not_Object()
    {
        await AssertRefusal(
            BuildOperationDocument("""
                                   {
                                     "operationId": "v2.test.create",
                                     "parameters": [null],
                                     "responses": { "204": { "description": "ok" } }
                                   }
                                   """),
            "operation 'v2.test.create': parameter must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Request_Body_Is_Not_Object()
    {
        await AssertRefusal(
            BuildOperationDocument("""
                                   {
                                     "operationId": "v2.test.create",
                                     "requestBody": [],
                                     "responses": { "204": { "description": "ok" } }
                                   }
                                   """),
            "operation 'v2.test.create' requestBody: requestBody must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Request_Body_Content_Is_Not_Object()
    {
        await AssertRefusal(
            BuildOperationDocument("""
                                   {
                                     "operationId": "v2.test.create",
                                     "requestBody": { "content": [] },
                                     "responses": { "204": { "description": "ok" } }
                                   }
                                   """),
            "operation 'v2.test.create' requestBody: requestBody content must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Request_Media_Entry_Is_Not_Object()
    {
        await AssertRefusal(
            BuildOperationDocument("""
                                   {
                                     "operationId": "v2.test.create",
                                     "requestBody": {
                                       "content": { "application/json": [] }
                                     },
                                     "responses": { "204": { "description": "ok" } }
                                   }
                                   """),
            "operation 'v2.test.create' requestBody content 'application/json': media entry must be an object");
    }

    [Test]
    public async Task Parse_Should_Batch_Malformed_Components_And_Paths()
    {
        var exception = await Assert
            .That(() => SpecFixture.Parse(BuildDocument("[]", "[]")))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Errors.Count).IsEqualTo(2);
        await Assert.That(exception.Errors[0]).Contains("document: 'components' must be an object");
        await Assert.That(exception.Errors[1]).Contains("document: 'paths' must be an object");
    }

    [Test]
    public async Task Parse_Should_Batch_Independent_Errors_Within_Operation()
    {
        var exception = await Assert
            .That(() => SpecFixture.Parse(BuildOperationDocument("""
                                                                 {
                                                                   "operationId": "v2.test.create",
                                                                   "parameters": {},
                                                                   "x-madeup": true,
                                                                   "responses": { "204": { "description": "ok" } }
                                                                 }
                                                                 """)))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Errors.Count).IsEqualTo(2);
        await Assert
            .That(exception.Errors.Any(error => error.Contains(
                "unknown operation key 'x-madeup'", StringComparison.Ordinal)))
            .IsTrue();
        await Assert
            .That(exception.Errors.Any(error => error.Contains(
                "parameters must be an array", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Parse_Should_Expose_Read_Only_Operations()
    {
        var document = SpecFixture.Parse(ReadOnlyFixture);

        await AssertReadOnly(document.Operations);
    }

    [Test]
    public async Task Parse_Should_Expose_Read_Only_Schemas()
    {
        var document = SpecFixture.Parse(ReadOnlyFixture);
        var collection = (ICollection<KeyValuePair<string, SchemaNode>>)document.Schemas;

        await Assert.That(collection.IsReadOnly).IsTrue();
        await Assert.That(collection.Clear).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Parse_Should_Expose_Read_Only_Object_Properties()
    {
        var document = SpecFixture.Parse(ReadOnlyFixture);
        var node = (ObjectNode)document.Schemas["Item"];

        await AssertReadOnly(node.Properties);
    }

    [Test]
    public async Task Parse_Should_Expose_Read_Only_Union_Branches()
    {
        var document = SpecFixture.Parse(ReadOnlyFixture);
        var node = (UnionNode)document.Schemas["Choice"];

        await AssertReadOnly(node.Branches);
    }

    private static async Task AssertRefusal(string documentJson, string expectedMessage)
    {
        var exception = await Assert
            .That(() => SpecFixture.Parse(documentJson))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains(expectedMessage);
    }

    private static async Task AssertReadOnly<T>(IReadOnlyList<T> items)
    {
        var collection = (ICollection<T>)items;
        await Assert.That(collection.IsReadOnly).IsTrue();
        await Assert.That(collection.Clear).Throws<NotSupportedException>();
    }

    private static string SpecFixtureDocumentForPaths(string pathsJson) =>
        BuildDocument("""{ "schemas": {} }""", pathsJson);

    private static string BuildOperationDocument(string operationJson) =>
        SpecFixtureDocumentForPaths($$"""
                                      {
                                        "/test": {
                                          "post": {{operationJson}}
                                        }
                                      }
                                      """);

    private static string BuildDocument(string componentsJson, string pathsJson) => $$"""
                                                                                      {
                                                                                        "openapi": "3.1.0",
                                                                                        "info": { "title": "fixture", "version": "0.0.0" },
                                                                                        "paths": {{pathsJson}},
                                                                                        "components": {{componentsJson}}
                                                                                      }
                                                                                      """;
}
