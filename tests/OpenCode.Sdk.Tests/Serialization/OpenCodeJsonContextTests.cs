using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Internal.Serialization;
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
        await Assert.That(user.Id).IsEqualTo("msg_1");
        await Assert.That(user.Text).IsEqualTo("hello");
        var serialized = _serializer.Serialize<ISessionMessageInfo>(user);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("user");
    }

    [Test]
    public async Task Deserialize_Should_Use_The_Last_Duplicate_Marker_For_A_Known_Variant()
    {
        var json = _fixtures.LoadJson("Serialization.duplicate-known-session-message-marker.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<SessionMessageUser>();
    }

    [Test]
    public async Task Deserialize_Should_Use_The_Last_Duplicate_Marker_For_An_Unknown_Variant()
    {
        var json = _fixtures.LoadJson("Serialization.duplicate-unknown-session-message-marker.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageInfo>();
        await Assert.That(((UnknownSessionMessageInfo)result).Type).IsEqualTo("future-message");
    }

    [Test]
    public async Task DeserializeAsyncEnumerable_Should_Dispatch_Unions_From_A_Partial_Reader()
    {
        var json = _fixtures.LoadJson("Serialization.stream-session-messages.json");
        var context = new OpenCodeJsonContext(new JsonSerializerOptions { DefaultBufferSize = 16 });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var messages = new List<ISessionMessageInfo>();

        await foreach (var message in JsonSerializer.DeserializeAsyncEnumerable(stream, context.ISessionMessageInfo))
        {
            messages.Add(message ?? throw new JsonException("The streamed fixture contained a null message."));
        }

        await Assert.That(messages.Count).IsEqualTo(3);
        await Assert.That(messages[0]).IsTypeOf<SessionMessageUser>();
        await Assert.That(messages[1]).IsTypeOf<UnknownSessionMessageInfo>();
        await Assert.That(messages[2]).IsTypeOf<SessionMessageCompactionRunning>();
    }

    [Test]
    public async Task DeserializeAsyncEnumerable_Should_Dispatch_Event_Unions_From_A_Partial_Reader()
    {
        var json = _fixtures.LoadJson("Serialization.stream-events.json");
        var context = new OpenCodeJsonContext(new JsonSerializerOptions { DefaultBufferSize = 16 });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var events = new List<IEvent>();

        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable(stream, context.IEvent))
        {
            events.Add(item ?? throw new JsonException("The streamed fixture contained a null event."));
        }

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0]).IsTypeOf<SessionCreated>();
        await Assert.That(events[1]).IsTypeOf<UnknownEvent>();
    }

    [Test]
    public async Task DeserializeAsync_Should_Read_A_Multi_Item_Page_Through_Partial_Readers()
    {
        var json = _fixtures.LoadJson("Serialization.stream-message-list-page.json");
        var context = new OpenCodeJsonContext(new JsonSerializerOptions { DefaultBufferSize = 16 });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var page = await JsonSerializer.DeserializeAsync(stream, context.MessageListResponseEnvelope);

        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Data.Count).IsEqualTo(2);
        await Assert.That(page.Data[0]).IsTypeOf<SessionMessageUser>();
        await Assert.That(page.Data[1]).IsTypeOf<SessionMessageCompactionRunning>();
        await Assert.That(page.Cursor.Next).IsEqualTo("cur_2");
    }

    [Test]
    public async Task DeserializeAsync_Should_Read_A_Union_Before_Trailing_Stream_Padding()
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message.json") + new string(' ', 128);
        var context = new OpenCodeJsonContext(new JsonSerializerOptions { DefaultBufferSize = 64 });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var message = await JsonSerializer.DeserializeAsync(stream, context.ISessionMessageInfo);

        await Assert.That(message).IsTypeOf<SessionMessageUser>();
    }

    [Test]
    public async Task Deserialize_Should_Collapse_Explicit_Null_On_An_Optional_Property()
    {
        var json = _fixtures.LoadJson("Serialization.null-parent-session.json");

        var session = _serializer.Deserialize<SessionInfo>(json);

        await Assert.That(session.ParentId).IsNull();
    }

    [Test]
    public async Task Carrier_Constructor_Should_Refuse_An_Unparsed_Payload()
    {
        var exception = Assert.Throws<ArgumentException>(() => _ = new UnknownOpenCodeError("future", default));

        await Assert.That(exception.ParamName).IsEqualTo("payload");
    }

    [Test]
    public async Task Carrier_Constructor_Should_Refuse_A_Payload_Without_The_Constructor_Marker()
    {
        using var document = JsonDocument.Parse(_fixtures.LoadJson("Serialization.unknown-error-without-marker.json"));

        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new UnknownOpenCodeError("future", document.RootElement));

        await Assert.That(exception.ParamName).IsEqualTo("payload");
    }

    [Test]
    public async Task Carrier_Constructor_Should_Refuse_A_Payload_With_A_Different_Marker()
    {
        using var document = JsonDocument.Parse(_fixtures.LoadJson("Serialization.unknown-error-with-different-marker.json"));

        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new UnknownOpenCodeError("future", document.RootElement));

        await Assert.That(exception.ParamName).IsEqualTo("payload");
    }

    [Test]
    public async Task Carrier_Constructor_Should_Refuse_A_Non_Object_Payload()
    {
        using var document = JsonDocument.Parse(_fixtures.LoadJson("Serialization.non-object-union-payload.json"));

        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new UnknownOpenCodeError("future", document.RootElement));

        await Assert.That(exception.ParamName).IsEqualTo("payload");
    }

    [Test]
    public async Task Deserialize_Should_Return_Null_For_A_Concrete_Unknown_Carrier_Without_Outer_Metadata()
    {
        var typeInfo = (JsonTypeInfo<UnknownOpenCodeError>)_serializer.GetTypeInfo(typeof(UnknownOpenCodeError));

        var result = JsonSerializer.Deserialize("null", typeInfo);

        await Assert.That(result).IsNull();
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
    public async Task Carrier_Constructor_Should_Refuse_A_Disagreeing_Fixed_Outer_Marker()
    {
        using var document = JsonDocument.Parse(_fixtures.LoadJson("Serialization.mismatched-compaction-marker.json"));

        var exception = Assert.Throws<ArgumentException>(() =>
            _ = new UnknownSessionMessageCompaction("paused", document.RootElement));

        await Assert.That(exception.ParamName).IsEqualTo("payload");
    }

    [Test]
    public async Task Carrier_Constructor_Should_Replay_A_Matching_Fixed_Outer_Marker()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-compaction-status.json");
        using var document = JsonDocument.Parse(json);
        var unknown = new UnknownSessionMessageCompaction("paused", document.RootElement);

        var roundTrip = _serializer.Serialize<ISessionMessageCompaction>(unknown);

        await Assert.That(roundTrip).IsEqualTo(json);
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
    public async Task Deserialize_Should_Accept_And_Omit_Explicit_Null_For_An_Optional_Special_Number()
    {
        var json = CreateShellJson(exit: null);

        var shell = (SessionMessageShell)_serializer.Deserialize<ISessionMessageInfo>(json);
        var serialized = _serializer.Serialize<ISessionMessageInfo>(shell);

        await Assert.That(shell.Exit).IsNull();
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.TryGetProperty("exit", out _)).IsFalse();
    }

    [Test]
    public async Task Deserialize_Should_Keep_Treating_An_Absent_Optional_Property_As_Absent()
    {
        var json = _fixtures.LoadJson("Serialization.known-session.json");

        var session = _serializer.Deserialize<SessionInfo>(json);

        await Assert.That(session.ParentId).IsNull();
    }

    [Test]
    public async Task Deserialize_Should_Keep_Absent_Optional_Collections_Null()
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<SessionMessageUser>();
        var user = (SessionMessageUser)result;
        await Assert.That(user.Metadata).IsNull();
        await Assert.That(user.Files).IsNull();
        await Assert.That(user.Agents).IsNull();
        await Assert.That(user.Skills).IsNull();
    }

    [Test]
    public async Task Deserialize_Should_Round_Trip_An_In_Band_Null_Dictionary_Value()
    {
        var json = _fixtures.LoadJson("Serialization.known-shell.json");

        var shell = _serializer.Deserialize<ShellInfo>(json);
        object? metadataValue = shell.Metadata["nullable"];

        await Assert.That(metadataValue).IsTypeOf<JsonElement>();
        await Assert.That(((JsonElement)metadataValue).ValueKind).IsEqualTo(JsonValueKind.Null);
        var serialized = _serializer.Serialize(shell);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("metadata").GetProperty("nullable").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Outer_Variant_Byte_For_Byte()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-session-message.json");

        var result = _serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageInfo>();
        var unknown = (UnknownSessionMessageInfo)result;
        await Assert.That(unknown.Type).IsEqualTo("future-message");
        await Assert.That(unknown.Payload.GetProperty("id").GetString()).IsEqualTo("msg_2");
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
    public async Task Serialize_Should_Replay_A_Valid_Hand_Constructed_Carrier_Through_Both_Metadata_Shapes()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-session-message.json");
        using var document = JsonDocument.Parse(json);
        var unknown = new UnknownSessionMessageInfo("future-message", document.RootElement);

        var throughInterface = _serializer.Serialize<ISessionMessageInfo>(unknown);
        var throughConcrete = _serializer.Serialize(unknown);

        await Assert.That(throughInterface).IsEqualTo(json);
        await Assert.That(throughConcrete).IsEqualTo(json);
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
        await Assert.That(running.Id).IsEqualTo("msg_4");
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
    public async Task Deserialize_Should_Accept_Explicit_Null_For_Optional_Collections()
    {
        var json = _fixtures.LoadJson("Serialization.null-optional-collection.json");

        var user = (SessionMessageUser)_serializer.Deserialize<ISessionMessageInfo>(json);

        await Assert.That(user.Id).IsEqualTo("msg_3");
        await Assert.That(user.Metadata).IsNull();
    }

    [Test]
    public async Task Deserialize_Should_Throw_JsonException_When_Union_Payload_Is_Not_An_Object()
    {
        var json = _fixtures.LoadJson("Serialization.non-object-union-payload.json");

        _ = await Assert.That(() => _serializer.Deserialize<ISessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task State_Should_Retain_The_Caller_Owned_Collection_Reference()
    {
        using var document = JsonDocument.Parse("null");
        var nullValue = document.RootElement.Clone();
        var state = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["original"] = nullValue,
        };
        var model = new SessionMessageAssistantReasoning
        {
            Text = "reasoning",
            State = state,
        };

        state.Add("mutation", nullValue);

        await Assert.That(model.State.ContainsKey("mutation")).IsTrue();
        await Assert.That(model.State.ContainsKey("original")).IsTrue();
    }

    [Test]
    public async Task Delta_Should_Retain_The_Required_Caller_Owned_Collection_Reference()
    {
        var delta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["original"] = "one",
        };
        var model = new SessionInstructionsUpdatedData
        {
            SessionId = "ses_1",
            Delta = delta,
        };

        delta.Add("mutation", "two");

        await Assert.That(model.Delta.ContainsKey("mutation")).IsTrue();
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
