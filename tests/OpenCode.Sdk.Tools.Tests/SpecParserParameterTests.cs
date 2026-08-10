using OpenCode.Sdk.Tools.Generator.Parsing;
using OpenCode.Sdk.Tools.Generator.Parsing.Operations;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserParameterTests
{
    [Test]
    public async Task Parse_Should_Record_Path_And_Query_Parameters()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session/{sessionID}/history": {
                                                  "get": {
                                                    "operationId": "v2.session.history",
                                                    "parameters": [
                                                      {
                                                        "name": "sessionID",
                                                        "in": "path",
                                                        "schema": { "type": "string", "pattern": "^ses" },
                                                        "required": true
                                                      },
                                                      { "name": "limit", "in": "query", "schema": { "type": "string" } },
                                                      { "name": "after", "in": "query", "schema": { "type": "string" } }
                                                    ],
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var parameters = document.Operations.Single().Parameters;
        var sessionId = parameters.Single(parameter => parameter.Name == "sessionID");
        await Assert.That(sessionId.Location).IsEqualTo(SpecParameterLocation.Path);
        await Assert.That(sessionId.IsRequired).IsTrue();
        var limit = parameters.Single(parameter => parameter.Name == "limit");
        await Assert.That(limit.Location).IsEqualTo(SpecParameterLocation.Query);
        await Assert.That(limit.IsRequired).IsFalse();
        await Assert.That(limit.IsDeepObject).IsFalse();
    }

    [Test]
    public async Task Parse_Should_Flag_Deep_Object_Parameters()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/fs/read/*": {
                                                  "get": {
                                                    "operationId": "v2.fs.read",
                                                    "parameters": [
                                                      {
                                                        "name": "location",
                                                        "in": "query",
                                                        "schema": {
                                                          "type": "object",
                                                          "properties": {
                                                            "directory": { "type": "string" }
                                                          },
                                                          "additionalProperties": false
                                                        },
                                                        "style": "deepObject",
                                                        "explode": true
                                                      }
                                                    ],
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var parameter = document.Operations.Single().Parameters.Single();
        await Assert.That(parameter.IsDeepObject).IsTrue();
        var reference = (RefNode)parameter.Schema;
        await Assert.That(reference.Target).IsEqualTo("op:v2.fs.read#/parameters/location");
        await Assert.That(document.Schemas[reference.Target]).IsTypeOf<ObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Keep_Bracketed_Parameter_Names_Verbatim()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/fs/read": {
                                                  "get": {
                                                    "operationId": "v2.fs.read",
                                                    "parameters": [
                                                      {
                                                        "name": "location[directory]",
                                                        "in": "query",
                                                        "schema": { "type": "string" }
                                                      },
                                                      {
                                                        "name": "location[workspace]",
                                                        "in": "query",
                                                        "schema": { "type": "string" }
                                                      }
                                                    ],
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var names = document.Operations.Single().Parameters.Select(static parameter => parameter.Name);
        await Assert.That(names).IsEquivalentTo(["location[directory]", "location[workspace]"]);
    }

    [Test]
    public async Task Parse_Should_Parse_Parameter_Schema_Through_Node_Parser()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/feature": {
                                                  "get": {
                                                    "operationId": "feature.get",
                                                    "parameters": [
                                                      {
                                                        "name": "enabled",
                                                        "in": "query",
                                                        "schema": {
                                                          "anyOf": [
                                                            { "type": "boolean" },
                                                            { "type": "string", "enum": ["true", "false"] }
                                                          ]
                                                        }
                                                      }
                                                    ],
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var parameterReference = (RefNode)document.Operations.Single().Parameters.Single().Schema;
        var union = (UnionNode)document.Schemas[parameterReference.Target];
        await Assert.That(((PrimitiveNode)union.Branches[0]).Kind).IsEqualTo(PrimitiveKind.Boolean);
        var enumReference = (RefNode)union.Branches[1];
        await Assert.That(document.Schemas[enumReference.Target]).IsTypeOf<EnumNode>();
    }

    [Test]
    public async Task Parse_Should_Record_Request_Body_With_Media_Type()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "post": {
                                                    "operationId": "v2.session.create",
                                                    "requestBody": {
                                                      "required": true,
                                                      "content": {
                                                        "application/json": {
                                                          "schema": {
                                                            "type": "object",
                                                            "properties": {
                                                              "title": { "type": "string" }
                                                            },
                                                            "additionalProperties": false
                                                          }
                                                        }
                                                      }
                                                    },
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        var requestBody = document.Operations.Single().RequestBody!;
        await Assert.That(requestBody.ContentType.IsJson).IsTrue();
        await Assert.That(requestBody.IsRequired).IsTrue();
        var reference = (RefNode)requestBody.Schema;
        await Assert.That(reference.Target).IsEqualTo("op:v2.session.create#/requestBody");
        await Assert.That(document.Schemas[reference.Target]).IsTypeOf<ObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Default_Request_Body_Required_To_False()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "post": {
                                                    "operationId": "v2.session.create",
                                                    "requestBody": {
                                                      "content": {
                                                        "text/plain": { "schema": { "type": "string" } }
                                                      }
                                                    },
                                                    "responses": { "204": { "description": "ok" } }
                                                  }
                                                }
                                              }
                                              """);

        await Assert.That(document.Operations.Single().RequestBody!.IsRequired).IsFalse();
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Path_Parameter_Is_Undeclared()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session/{id}": {
                                                   "get": {
                                                     "operationId": "v2.session.get",
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("id");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Declared_Path_Parameter_Missing_From_Template()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "operationId": "v2.session.get",
                                                     "parameters": [
                                                       {
                                                         "name": "id",
                                                         "in": "path",
                                                         "schema": { "type": "string" },
                                                         "required": true
                                                       }
                                                     ],
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("id");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Parameter_Style_Is_Unknown()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "operationId": "v2.session.list",
                                                     "parameters": [
                                                       {
                                                         "name": "filter",
                                                         "in": "query",
                                                         "schema": { "type": "string" },
                                                         "style": "form",
                                                         "explode": true
                                                       }
                                                     ],
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("form");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Parameter_Location_Is_Unknown()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "operationId": "v2.session.list",
                                                     "parameters": [
                                                       {
                                                         "name": "sid",
                                                         "in": "cookie",
                                                         "schema": { "type": "string" }
                                                       }
                                                     ],
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("cookie");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Parameter_Is_Duplicated()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "get": {
                                                     "operationId": "v2.session.list",
                                                     "parameters": [
                                                       { "name": "limit", "in": "query", "schema": { "type": "string" } },
                                                       { "name": "limit", "in": "query", "schema": { "type": "integer" } }
                                                     ],
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("duplicate parameter");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Request_Body_Has_Multiple_Content_Types()
    {
        var ex = await Assert
            .That(() => SpecFixture.ParsePaths("""
                                               {
                                                 "/api/session": {
                                                   "post": {
                                                     "operationId": "v2.session.create",
                                                     "requestBody": {
                                                       "content": {
                                                         "application/json": { "schema": { "type": "string" } },
                                                         "text/plain": { "schema": { "type": "string" } }
                                                       }
                                                     },
                                                     "responses": { "204": { "description": "ok" } }
                                                   }
                                                 }
                                               }
                                               """))
            .Throws<SpecParseException>();

        await Assert.That(ex!.Message).Contains("exactly one media entry");
    }
}
