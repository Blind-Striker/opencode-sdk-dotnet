using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Parsing;
using OpenCode.Sdk.Tools.Generator.Parsing.Operations;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecParserResponseTests
{
    [Test]
    public async Task Parse_Should_Classify_Cursor_Envelope_Behind_Named_Ref()
    {
        var document = SpecFixture.ParsePaths("""
                                              {
                                                "/api/session": {
                                                  "get": {
                                                    "operationId": "v2.session.list",
                                                    "responses": {
                                                      "200": {
                                                        "description": "SessionsResponse",
                                                        "content": {
                                                          "application/json": {
                                                            "schema": { "$ref": "#/components/schemas/SessionsResponse" }
                                                          }
                                                        }
                                                      }
                                                    }
                                                  }
                                                }
                                              }
                                              """, schemasJson: """
                                                                {
                                                                  "SessionsResponse": {
                                                                    "type": "object",
                                                                    "properties": {
                                                                      "data": { "type": "array", "items": { "type": "string" } },
                                                                      "cursor": { "type": "object", "properties": {}, "additionalProperties": false }
                                                                    },
                                                                    "required": ["data", "cursor"],
                                                                    "additionalProperties": false
                                                                  }
                                                                }
                                                                """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(response.ContentType!.IsJson).IsTrue();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.CursorData);
        await Assert.That(response.IsSse).IsFalse();
    }

    [Test]
    public async Task Parse_Should_Record_Json_Response_With_Status()
    {
        var document = ParseResponses("""
                                      {
                                        "201": {
                                          "description": "Created",
                                          "content": {
                                            "application/json": {
                                              "schema": { "type": "string" }
                                            }
                                          }
                                        }
                                      }
                                      """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.StatusCode).IsEqualTo(201);
        await Assert.That(response.Description).IsEqualTo("Created");
        await Assert.That(response.ContentType!.Raw).IsEqualTo("application/json");
        await Assert.That(response.Schema).IsTypeOf<PrimitiveNode>();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
    }

    [Test]
    public async Task Parse_Should_Record_No_Content_Response()
    {
        var document = ParseResponses("""
                                      {
                                        "204": { "description": "No content" }
                                      }
                                      """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.StatusCode).IsEqualTo(204);
        await Assert.That(response.ContentType).IsNull();
        await Assert.That(response.Schema).IsNull();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.None);
        await Assert.That(response.IsSse).IsFalse();
        await Assert.That(response.EffectStreamMetadata).IsNull();
    }

    [Test]
    public async Task Parse_Should_Sort_Responses_By_Status()
    {
        var document = ParseResponses("""
                                      {
                                        "404": { "description": "Missing" },
                                        "200": { "description": "Success" }
                                      }
                                      """);

        var responses = document.Operations.Single().Responses;
        await Assert.That(responses.Count).IsEqualTo(2);
        await Assert.That(responses[0].StatusCode).IsEqualTo(200);
        await Assert.That(responses[1].StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task Parse_Should_Expose_Read_Only_Responses()
    {
        var document = ParseResponses("""
                                      {
                                        "200": { "description": "Success" }
                                      }
                                      """);

        var responses = document.Operations.Single().Responses;
        var collection = (ICollection<SpecResponse>)responses;
        await Assert.That(collection.IsReadOnly).IsTrue();
        await Assert.That(collection.Clear).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Parse_Should_Classify_Data_Envelope()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "Success",
                                          "content": {
                                            "application/json": {
                                              "schema": {
                                                "type": "object",
                                                "properties": {
                                                  "data": { "type": "string" }
                                                },
                                                "required": ["data"],
                                                "additionalProperties": false
                                              }
                                            }
                                          }
                                        }
                                      }
                                      """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Data);
    }

    [Test]
    public async Task Parse_Should_Classify_Data_Location_Envelope()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "Success",
                                          "content": {
                                            "application/json": {
                                              "schema": {
                                                "type": "object",
                                                "properties": {
                                                  "location": { "$ref": "#/components/schemas/LocationInfo" },
                                                  "data": {
                                                    "type": "array",
                                                    "items": { "$ref": "#/components/schemas/AgentV2Info" }
                                                  }
                                                },
                                                "required": ["location", "data"],
                                                "additionalProperties": false
                                              }
                                            }
                                          }
                                        }
                                      }
                                      """, schemasJson: """
                                                        {
                                                          "LocationInfo": {
                                                            "type": "object",
                                                            "properties": {
                                                              "directory": { "type": "string" },
                                                              "workspaceID": { "type": "string", "pattern": "^wrk" },
                                                              "project": {
                                                                "type": "object",
                                                                "properties": {
                                                                  "id": { "type": "string" },
                                                                  "directory": { "type": "string" }
                                                                },
                                                                "required": ["id", "directory"],
                                                                "additionalProperties": false
                                                              }
                                                            },
                                                            "required": ["directory", "project"],
                                                            "additionalProperties": false
                                                          },
                                                          "AgentV2Info": { "type": "string" }
                                                        }
                                                        """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.DataLocation);
    }

    [Test]
    public async Task Parse_Should_Classify_Has_More_Envelope_Behind_Named_Ref()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "SessionHistory",
                                          "content": {
                                            "application/json": {
                                              "schema": { "$ref": "#/components/schemas/SessionHistory" }
                                            }
                                          }
                                        }
                                      }
                                      """, schemasJson: """
                                                        {
                                                          "SessionDurableEvent": { "type": "string" },
                                                          "SessionHistory": {
                                                            "type": "object",
                                                            "properties": {
                                                              "data": {
                                                                "type": "array",
                                                                "items": { "$ref": "#/components/schemas/SessionDurableEvent" }
                                                              },
                                                              "hasMore": { "type": "boolean" }
                                                            },
                                                            "required": ["data", "hasMore"],
                                                            "additionalProperties": false
                                                          }
                                                        }
                                                        """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.DataHasMore);
    }

    [Test]
    public async Task Parse_Should_Classify_Other_Object_Shapes_As_Bare()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "Success",
                                          "content": {
                                            "application/json": {
                                              "schema": {
                                                "type": "object",
                                                "properties": {
                                                  "info": { "type": "string" },
                                                  "parts": { "type": "array", "items": { "type": "string" } }
                                                },
                                                "required": ["info", "parts"],
                                                "additionalProperties": false
                                              }
                                            }
                                          }
                                        }
                                      }
                                      """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
    }

    [Test]
    public async Task Parse_Should_Classify_Data_With_Extra_Property_As_Bare()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "Success",
                                          "content": {
                                            "application/json": {
                                              "schema": {
                                                "type": "object",
                                                "properties": {
                                                  "data": { "type": "string" },
                                                  "extra": { "type": "boolean" }
                                                },
                                                "required": ["data", "extra"],
                                                "additionalProperties": false
                                              }
                                            }
                                          }
                                        }
                                      }
                                      """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
    }

    [Test]
    public async Task Parse_Should_Classify_Non_Json_Content_As_Bare()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "Download",
                                          "content": {
                                            "application/octet-stream": {
                                              "schema": { "type": "string", "format": "binary" }
                                            }
                                          }
                                        }
                                      }
                                      """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.ContentType!.Stripped).IsEqualTo("application/octet-stream");
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
    }

    [Test]
    public async Task Parse_Should_Detect_Sse_Response_And_Flag_Operation()
    {
        var document = ParseResponses("""
                                      {
                                        "204": { "description": "No content" },
                                        "200": {
                                          "description": "Events",
                                          "content": {
                                            "text/event-stream; charset=utf-8": {
                                              "schema": { "$ref": "#/components/schemas/EventStream" }
                                            }
                                          }
                                        }
                                      }
                                      """, schemasJson: """
                                                        {
                                                          "EventStream": { "type": "string" }
                                                        }
                                                        """);

        var operation = document.Operations.Single();
        var response = operation.Responses.Single(candidate => candidate.StatusCode == 200);
        await Assert.That(response.ContentType!.Stripped).IsEqualTo("text/event-stream");
        await Assert.That(response.IsSse).IsTrue();
        await Assert.That(response.EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
        await Assert.That(operation.IsSse).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Carry_Effect_Stream_Metadata_Opaque()
    {
        var document = ParseResponses("""
                                      {
                                        "200": {
                                          "description": "Events",
                                          "content": {
                                            "text/event-stream": {
                                              "schema": {
                                                "type": "object",
                                                "properties": {
                                                  "id": { "type": "string" },
                                                  "event": { "type": "string" },
                                                  "data": { "$ref": "#/components/schemas/SessionDurableEventStream" }
                                                },
                                                "required": ["id", "event", "data"],
                                                "additionalProperties": false
                                              },
                                              "x-effect-stream": {"encoding":"json","causeSchema":{"not":{}}}
                                            }
                                          }
                                        }
                                      }
                                      """, schemasJson: """
                                                        {
                                                          "SessionDurableEvent": { "type": "string" },
                                                          "SessionDurableEventStream": {
                                                            "type": "string",
                                                            "contentSchema": { "$ref": "#/components/schemas/SessionDurableEvent" },
                                                            "contentMediaType": "application/json"
                                                          }
                                                        }
                                                        """);

        var response = document.Operations.Single().Responses.Single();
        await Assert.That(response.EffectStreamMetadata.HasValue).IsTrue();
        var metadata = response.EffectStreamMetadata.GetValueOrDefault();
        using var expected = JsonDocument.Parse("""{"encoding":"json","causeSchema":{"not":{}}}""");
        await Assert.That(JsonElement.DeepEquals(metadata, expected.RootElement)).IsTrue();
        var reference = (RefNode)response.Schema!;
        await Assert.That(reference.Target).IsEqualTo("op:v2.test.get#/responses/200");
        await Assert.That(document.Schemas[reference.Target]).IsTypeOf<ObjectNode>();
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Has_Multiple_Content_Types()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Success",
                                           "content": {
                                             "application/json": { "schema": { "type": "string" } },
                                             "text/plain": { "schema": { "type": "string" } }
                                           }
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("response content must contain exactly one media entry");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Responses_Is_Not_Object()
    {
        var exception = await Assert
            .That(() => ParseResponses("[]"))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("responses must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Is_Not_Object()
    {
        var exception = await Assert
            .That(() => ParseResponses("""{ "200": [] }"""))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("response must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Content_Is_Not_Object()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": { "description": "Success", "content": [] }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("response content must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Content_Is_Empty()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": { "description": "Success", "content": {} }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("response content must contain exactly one media entry");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Media_Entry_Is_Not_Object()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Success",
                                           "content": { "application/json": [] }
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("media entry must be an object");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Effect_Stream_Appears_On_Json_Media()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Success",
                                           "content": {
                                             "application/json": {
                                               "schema": { "type": "string" },
                                               "x-effect-stream": { "encoding": "json" }
                                             }
                                           }
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("x-effect-stream");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Media_Key_Is_Unknown()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Success",
                                           "content": {
                                             "application/json": {
                                               "schema": { "type": "string" },
                                               "examples": {}
                                             }
                                           }
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("examples");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Key_Is_Unknown()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Success",
                                           "headers": {}
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("headers");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Media_Schema_Is_Missing()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Success",
                                           "content": {
                                             "application/json": {}
                                           }
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("media entry schema is required");
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Response_Status_Is_Not_Numeric()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "default": { "description": "Fallback" }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("default");
    }

    [Test]
    public async Task Parse_Should_Batch_Independent_Response_Errors()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": [],
                                         "default": { "description": "Fallback" }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Errors.Count).IsEqualTo(2);
        await Assert.That(exception.Errors.Any(error => error.Contains("response must be an object", StringComparison.Ordinal))).IsTrue();
        await Assert.That(exception.Errors.Any(error => error.Contains("default", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Parse_Should_Refuse_When_Ref_Chain_Cycles_During_Envelope_Classification()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Cycle",
                                           "content": {
                                             "application/json": {
                                               "schema": { "$ref": "#/components/schemas/A" }
                                             }
                                           }
                                         }
                                       }
                                       """, schemasJson: """
                                                         {
                                                           "A": { "$ref": "#/components/schemas/B" },
                                                           "B": { "$ref": "#/components/schemas/A" }
                                                         }
                                                         """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("circular ref during envelope classification");
    }

    [Test]
    public async Task Parse_Should_Include_Response_Schemas_In_Dangling_Ref_Sweep()
    {
        var exception = await Assert
            .That(() => ParseResponses("""
                                       {
                                         "200": {
                                           "description": "Missing",
                                           "content": {
                                             "application/json": {
                                               "schema": { "$ref": "#/components/schemas/MissingResponse" }
                                             }
                                           }
                                         }
                                       }
                                       """))
            .Throws<SpecParseException>();

        await Assert.That(exception!.Message).Contains("operation 'v2.test.get' response 200: unresolved ref 'MissingResponse'");
    }

    private static SpecDocument ParseResponses(string responsesJson, string schemasJson = "{}") =>
        SpecFixture.ParsePaths($$"""
                                 {
                                   "/api/test": {
                                     "get": {
                                       "operationId": "v2.test.get",
                                       "responses": {{responsesJson}}
                                     }
                                   }
                                 }
                                 """, schemasJson);
}
