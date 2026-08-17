using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Serialization;

public sealed class OpenCodeJsonContextTests
{
    private readonly FixtureLoader _fixtures = new();
    private readonly GeneratedJsonSerializer _serializer = new();

    [Test]
    public async Task Deserialize_Should_Create_Known_Outer_Variant_When_Marker_Is_Last()
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<SessionMessageUser>();
        var user = (SessionMessageUser)result;
        await Assert.That(user.Id).IsEqualTo("message-1");
        await Assert.That(user.Text).IsEqualTo("hello");
        var serialized = _serializer.Serialize<ISessionMessageInfo>(user);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("user");
    }

    [Test]
    public async Task Deserialize_Should_Reject_Explicit_Null_On_An_Optional_Nonnull_Property()
    {
        var json = _fixtures.LoadJson("Serialization.null-parent-session.json");

        _ = await Assert.That(() => _serializer.Deserialize<SessionInfo>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Carrier_Constructor_Should_Refuse_An_Unparsed_Payload()
    {
        var exception = Assert.Throws<ArgumentException>(() => _ = new UnknownOpenCodeError("future", default));

        await Assert.That(exception.ParamName).IsEqualTo("payload");
    }

    [Test]
    public async Task Deserialize_Should_Reject_Null_For_A_Concrete_Unknown_Carrier()
    {
        var typeInfo = (JsonTypeInfo<UnknownOpenCodeError>)_serializer.GetTypeInfo(typeof(UnknownOpenCodeError));

        _ = await Assert.That(() => JsonSerializer.Deserialize("null", typeInfo)).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Reject_A_Contradicted_Fixed_Marker_On_The_Concrete_Carrier()
    {
        var json = _fixtures.LoadJson("Serialization.mismatched-compaction-marker.json");
        var typeInfo = (JsonTypeInfo<UnknownSessionMessageCompaction>)_serializer.GetTypeInfo(typeof(UnknownSessionMessageCompaction));

        _ = await Assert.That(() => JsonSerializer.Deserialize(json, typeInfo)).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Reject_A_Contradicted_Fixed_Marker_On_The_Union_Base()
    {
        var json = _fixtures.LoadJson("Serialization.mismatched-compaction-marker.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageCompaction>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Reject_A_Numeric_String_Enum_Value()
    {
        var json = _fixtures.LoadJson("Serialization.numeric-diff-status.json");

        _ = await Assert.That(() => _serializer.Deserialize<FileDiffInfo>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Map_A_Known_String_Enum_Wire_Value()
    {
        var json = _fixtures.LoadJson("Serialization.known-diff-status.json");

        var diff = _serializer.Deserialize<FileDiffInfo>(json);

        await Assert.That(diff.Status).IsEqualTo(FileDiffInfoStatus.Modified);
    }

    [Arguments("NaN", double.NaN)]
    [Arguments("Infinity", double.PositiveInfinity)]
    [Arguments("-Infinity", double.NegativeInfinity)]
    [Test]
    public async Task Deserialize_Should_Round_Trip_A_Declared_Special_Number(string wireValue, double expected)
    {
        var json = CreateShellJson(JsonValue.Create(wireValue));

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<SessionMessageShell>();
        var shell = (SessionMessageShell)result;
        await Assert.That(shell.Exit).IsEqualTo(expected);
        var roundTrip = _serializer.Serialize<ISessionMessageInfo>(shell);
        using var document = JsonDocument.Parse(roundTrip);
        var exit = document.RootElement.GetProperty("exit");
        await Assert.That(exit.ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(exit.GetString()).IsEqualTo(wireValue);
    }

    [Test]
    public async Task Deserialize_Should_Preserve_An_Ordinary_Finite_Special_Number()
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message-shell.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        var shell = (SessionMessageShell)result;
        await Assert.That(shell.Exit).IsEqualTo(7.25);
        var roundTrip = _serializer.Serialize<ISessionMessageInfo>(shell);
        using var document = JsonDocument.Parse(roundTrip);
        await Assert.That(document.RootElement.GetProperty("exit").ValueKind).IsEqualTo(JsonValueKind.Number);
    }

    [Test]
    public async Task Deserialize_Should_Reject_An_Arbitrary_Numeric_String_For_A_Special_Number()
    {
        var json = CreateShellJson(JsonValue.Create("7.25"));

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageInfo>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Reject_Explicit_Null_For_An_Optional_Special_Number()
    {
        var json = CreateShellJson(exit: null);

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageInfo>(json)).Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Keep_Treating_An_Absent_Optional_Property_As_Absent()
    {
        var json = _fixtures.LoadJson("Serialization.known-session.json");

        var session = _serializer.Deserialize<SessionInfo>(json);

        await Assert.That(session.ParentId).IsNull();
    }

    [Test]
    public async Task Deserialize_Should_Normalize_Absent_Optional_Nonnull_Collections_To_Empty()
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<SessionMessageUser>();
        var user = (SessionMessageUser)result;
        await Assert.That(user.Metadata).IsEmpty();
        await Assert.That(user.Files).IsEmpty();
        await Assert.That(user.Agents).IsEmpty();
        await Assert.That(user.Skills).IsEmpty();
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Outer_Variant_Byte_For_Byte()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-session-message.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageInfo>();
        var unknown = (UnknownSessionMessageInfo)result;
        await Assert.That(unknown.Type).IsEqualTo("future-message");
        var roundTrip = _serializer.Serialize<ISessionMessageInfo>(unknown);
        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Serialize_Should_Reproduce_The_Raw_Document_Through_The_Concrete_Carrier_Type()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-session-message.json");
        var unknown = (UnknownSessionMessageInfo)_serializer.Deserialize<ISessionMessageInfo>(json);

        var roundTrip = _serializer.Serialize(unknown);

        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Deserialize_Should_Preserve_The_Payload_Through_The_Concrete_Carrier_Type()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-session-message.json");

        var unknown = _serializer.Deserialize<UnknownSessionMessageInfo>(json);

        await Assert.That(unknown.Type).IsEqualTo("future-message");
        await Assert.That(_serializer.Serialize(unknown)).IsEqualTo(json);
    }

    [Test]
    public async Task Serialize_Should_Reproduce_The_Raw_Error_Through_The_Concrete_Carrier_Type()
    {
        const string json = "{\"_tag\":\"BrandNewError\",\"detail\":{\"code\":7}}";
        var unknown = (UnknownOpenCodeError)_serializer.Deserialize<IOpenCodeError>(json);

        var roundTrip = _serializer.Serialize(unknown);

        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Deserialize_Should_Create_Nested_Compaction_Variant_Through_The_Outer_Union()
    {
        var json = _fixtures.LoadJson("Serialization.known-compaction-message.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<SessionMessageCompactionRunning>();
        var running = (SessionMessageCompactionRunning)result;
        await Assert.That(running.Type).IsEqualTo("compaction");
        await Assert.That(running.Status).IsEqualTo("running");
        await Assert.That(running.Reason).IsEqualTo(SessionMessageCompactionRunningReason.Auto);
        var serialized = _serializer.Serialize<ISessionMessageInfo>(running);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("compaction");
        await Assert.That(document.RootElement.GetProperty("status").GetString()).IsEqualTo("running");
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Compaction_Status_Byte_For_Byte()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-compaction-status.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageCompaction>();
        var unknown = (UnknownSessionMessageCompaction)result;
        await Assert.That(unknown.Type).IsEqualTo("compaction");
        await Assert.That(unknown.Status).IsEqualTo("paused");
        var roundTrip = _serializer.Serialize<ISessionMessageInfo>(unknown);
        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Deserialize_Should_Create_Known_Assistant_Content_When_Marker_Is_Last()
    {
        var json = _fixtures.LoadJson("Serialization.known-assistant-content.json");

        var result = _serializer.Deserialize<ISessionMessageAssistantContent>(json);

        await Assert.That(result).IsTypeOf<SessionMessageAssistantText>();
        var text = (SessionMessageAssistantText)result;
        await Assert.That(text.Text).IsEqualTo("answer");
        var serialized = _serializer.Serialize<ISessionMessageAssistantContent>(text);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("text");
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Assistant_Content_Semantically()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-assistant-content.json");

        var result = _serializer.Deserialize<ISessionMessageAssistantContent>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageAssistantContent>();
        var unknown = (UnknownSessionMessageAssistantContent)result;
        var roundTrip = _serializer.Serialize<ISessionMessageAssistantContent>(unknown);
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(roundTrip);
        await Assert.That(JsonElement.DeepEquals(expected.RootElement, actual.RootElement)).IsTrue();
    }

    [Test]
    public async Task Deserialize_Should_Create_Known_Tool_State_When_Marker_Is_Last()
    {
        var json = _fixtures.LoadJson("Serialization.known-tool-state.json");

        var result = _serializer.Deserialize<ISessionMessageToolState>(json);

        await Assert.That(result).IsTypeOf<SessionMessageToolStateRunning>();
        var running = (SessionMessageToolStateRunning)result;
        await Assert.That(running.Input["query"].GetString()).IsEqualTo("queued input");
        var serialized = _serializer.Serialize<ISessionMessageToolState>(running);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("status").GetString()).IsEqualTo("running");
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Tool_State_Byte_For_Byte()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-tool-state.json");

        var result = _serializer.Deserialize<ISessionMessageToolState>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageToolState>();
        var unknown = (UnknownSessionMessageToolState)result;
        await Assert.That(unknown.Status).IsEqualTo("paused");
        var roundTrip = _serializer.Serialize<ISessionMessageToolState>(unknown);
        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Deserialize_Should_Throw_When_Union_Marker_Is_Missing()
    {
        var json = _fixtures.LoadJson("Serialization.missing-assistant-marker.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Throw_When_Union_Marker_Is_Empty()
    {
        var json = _fixtures.LoadJson("Serialization.empty-assistant-marker.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Throw_When_Union_Marker_Has_The_Wrong_Type()
    {
        var json = _fixtures.LoadJson("Serialization.malformed-assistant-marker.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Reject_Explicit_Null_For_Optional_Nonnull_Collections()
    {
        var json = _fixtures.LoadJson("Serialization.null-optional-collection.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageInfo>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Throw_JsonException_When_Union_Payload_Is_Not_An_Object()
    {
        var json = _fixtures.LoadJson("Serialization.non-object-assistant-payload.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task State_Should_Defensively_Copy_The_Input_Collection()
    {
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["original"] = default,
        };
        var model = new SessionMessageAssistantReasoning
        {
            Text = "reasoning",
            State = state,
        };

        state.Add("mutation", default);

        await Assert.That(model.State.ContainsKey("mutation")).IsFalse();
        await Assert.That(model.State.ContainsKey("original")).IsTrue();
    }

    [Test]
    public async Task GetTypeInfo_Should_Resolve_Union_Metadata_Without_Reflection_Fallback()
    {
        await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
        await Assert.That(_serializer.GetTypeInfo(typeof(ISessionMessageInfo))).IsNotNull();
        await Assert.That(_serializer.GetTypeInfo(typeof(ISessionMessageCompaction))).IsNotNull();
        await Assert.That(_serializer.GetTypeInfo(typeof(ISessionMessageAssistantContent))).IsNotNull();
        await Assert.That(_serializer.GetTypeInfo(typeof(ISessionMessageToolState))).IsNotNull();
    }

    private string CreateShellJson(JsonNode? exit)
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message-shell.json");
        var shell = JsonNode.Parse(json)?.AsObject()
                    ?? throw new InvalidOperationException("The session shell fixture must contain a JSON object.");
        shell["exit"] = exit;
        return shell.ToJsonString();
    }
}
