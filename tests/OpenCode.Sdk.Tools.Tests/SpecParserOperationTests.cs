using OpenCode.Sdk.Tools.Generator.Parsing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserOperationTests
{
    [Test]
    public async Task Parse_Should_Split_Modern_Surface_And_Segments()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "get": {
                                                    "operationId": "v2.session.list",
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var operation = document.Operations.Single();
        await Assert.That(operation.OperationId).IsEqualTo("v2.session.list");
        await Assert.That(operation.Surface).IsEqualTo(SpecSurface.Modern);
        await Assert.That(operation.Segments).IsEquivalentTo(["session", "list"]);
        await Assert.That(operation.Method).IsEqualTo("get");
        await Assert.That(operation.Path).IsEqualTo("/api/session");
    }

    [Test]
    public async Task Parse_Should_Keep_Legacy_Segments_Intact()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "get": {
                                                    "operationId": "session.get",
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var operation = document.Operations.Single();
        await Assert.That(operation.Surface).IsEqualTo(SpecSurface.Legacy);
        await Assert.That(operation.Segments).IsEquivalentTo(["session", "get"]);
    }

    [Test]
    public async Task Parse_Should_Record_Deep_Group_Segments()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session/permissions": {
                                                  "post": {
                                                    "operationId": "v2.session.permissions.respond",
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var operation = document.Operations.Single();
        await Assert.That(operation.Segments).IsEquivalentTo(["session", "permissions", "respond"]);
    }

    [Test]
    public async Task Parse_Should_Flag_Wildcard_Path()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/fs/read/*": {
                                                  "get": {
                                                    "operationId": "v2.fs.read",
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var operation = document.Operations.Single();
        await Assert.That(operation.HasWildcardPath).IsTrue();
        await Assert.That(operation.Path).IsEqualTo("/api/fs/read/*");
    }

    [Test]
    public async Task Parse_Should_Record_Summary_Description_And_Deprecated()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "get": {
                                                    "operationId": "v2.session.list",
                                                    "summary": "List sessions",
                                                    "description": "Lists active sessions.",
                                                    "deprecated": true,
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var operation = document.Operations.Single();
        await Assert.That(operation.Summary).IsEqualTo("List sessions");
        await Assert.That(operation.Description).IsEqualTo("Lists active sessions.");
        await Assert.That(operation.IsDeprecated).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Record_WebSocket_Flag()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/pty/connect": {
                                                  "get": {
                                                    "operationId": "v2.pty.connect",
                                                    "x-websocket": true,
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        await Assert.That(document.Operations.Single().IsWebSocket).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Ignore_Tags_Security_And_Code_Samples()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "get": {
                                                    "operationId": "v2.session.list",
                                                    "tags": ["session"],
                                                    "security": [{ "bearerAuth": [] }],
                                                    "x-codeSamples": [{ "lang": "curl", "source": "curl /api/session" }],
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        await Assert.That(document.Operations.Single().OperationId).IsEqualTo("v2.session.list");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Operation_Id_Is_Missing()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("operationId");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Operation_Id_Is_Duplicated()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "operationId": "v2.session.list",
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 },
                                                 "/api/session/recent": {
                                                   "get": {
                                                     "operationId": "v2.session.list",
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("v2.session.list");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Method_Is_Unknown()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "options": {
                                                     "operationId": "v2.session.options",
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("options");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Wildcard_Is_Not_Trailing()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/*/read": {
                                                   "get": {
                                                     "operationId": "v2.fs.read",
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("/api/*/read");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Operation_Key_Is_Unknown()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "operationId": "v2.session.list",
                                                     "x-madeup": true,
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("x-madeup");
    }

    [Test]
    public async Task Parse_Should_List_Operations_In_Document_Order()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/first": {
                                                  "post": {
                                                    "operationId": "first.post",
                                                    "responses": { "204": { "description": "ok" } }
                                                  },
                                                  "get": {
                                                    "operationId": "first.get",
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                },
                                                "/api/second": {
                                                  "delete": {
                                                    "operationId": "second.delete",
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        await Assert.That(document.Operations.Count).IsEqualTo(3);
        await Assert.That(document.Operations[0].OperationId).IsEqualTo("first.post");
        await Assert.That(document.Operations[1].OperationId).IsEqualTo("first.get");
        await Assert.That(document.Operations[2].OperationId).IsEqualTo("second.delete");
    }
}
