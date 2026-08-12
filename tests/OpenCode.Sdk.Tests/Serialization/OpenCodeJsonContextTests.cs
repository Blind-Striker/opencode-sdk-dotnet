using System.Text.Json;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests.Serialization;

public sealed class OpenCodeJsonContextTests
{
    private readonly FixtureLoader _fixtures = new();
    private readonly GeneratedJsonSerializer _serializer = new();

    [Test]
    public async Task Deserialize_Should_Create_Known_Outer_Variant_When_Marker_Is_Last()
    {
        var json = _fixtures.LoadJson("Serialization.known-session-message.json");

        var result = _serializer.Deserialize<SessionMessage>(json);

        await Assert.That(result).IsTypeOf<SessionMessageUser>();
        var user = (SessionMessageUser)result;
        await Assert.That(user.ID).IsEqualTo("message-1");
        await Assert.That(user.Text).IsEqualTo("hello");
        var serialized = _serializer.Serialize<SessionMessage>(user);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("user");
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Outer_Variant_Byte_For_Byte()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-session-message.json");

        var result = _serializer.Deserialize<SessionMessage>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessage>();
        var unknown = (UnknownSessionMessage)result;
        await Assert.That(unknown.Type).IsEqualTo("future-message");
        var roundTrip = _serializer.Serialize<SessionMessage>(unknown);
        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Deserialize_Should_Create_Known_Assistant_Content_When_Marker_Is_Last()
    {
        var json = _fixtures.LoadJson("Serialization.known-assistant-content.json");

        var result = _serializer.Deserialize<SessionMessageAssistantContent>(json);

        await Assert.That(result).IsTypeOf<SessionMessageAssistantText>();
        var text = (SessionMessageAssistantText)result;
        await Assert.That(text.Text).IsEqualTo("answer");
        var serialized = _serializer.Serialize<SessionMessageAssistantContent>(text);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("text");
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Assistant_Content_Semantically()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-assistant-content.json");

        var result = _serializer.Deserialize<SessionMessageAssistantContent>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageAssistantContent>();
        var unknown = (UnknownSessionMessageAssistantContent)result;
        var roundTrip = _serializer.Serialize<SessionMessageAssistantContent>(unknown);
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(roundTrip);
        await Assert.That(JsonElement.DeepEquals(expected.RootElement, actual.RootElement)).IsTrue();
    }

    [Test]
    public async Task Deserialize_Should_Create_Known_Tool_State_When_Marker_Is_Last()
    {
        var json = _fixtures.LoadJson("Serialization.known-tool-state.json");

        var result = _serializer.Deserialize<SessionMessageToolState>(json);

        await Assert.That(result).IsTypeOf<SessionMessageToolStatePending>();
        var pending = (SessionMessageToolStatePending)result;
        await Assert.That(pending.Input).IsEqualTo("queued input");
        var serialized = _serializer.Serialize<SessionMessageToolState>(pending);
        using var document = JsonDocument.Parse(serialized);
        await Assert.That(document.RootElement.GetProperty("status").GetString()).IsEqualTo("pending");
    }

    [Test]
    public async Task Deserialize_Should_Preserve_Unknown_Tool_State_Byte_For_Byte()
    {
        var json = _fixtures.LoadJson("Serialization.unknown-tool-state.json");

        var result = _serializer.Deserialize<SessionMessageToolState>(json);

        await Assert.That(result).IsTypeOf<UnknownSessionMessageToolState>();
        var unknown = (UnknownSessionMessageToolState)result;
        await Assert.That(unknown.Status).IsEqualTo("paused");
        var roundTrip = _serializer.Serialize<SessionMessageToolState>(unknown);
        await Assert.That(roundTrip).IsEqualTo(json);
    }

    [Test]
    public async Task Deserialize_Should_Throw_When_Union_Marker_Is_Missing()
    {
        var json = _fixtures.LoadJson("Serialization.missing-assistant-marker.json");

        _ = await Assert.That(() => _serializer.Deserialize<SessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Throw_When_Union_Marker_Has_The_Wrong_Type()
    {
        var json = _fixtures.LoadJson("Serialization.malformed-assistant-marker.json");

        _ = await Assert.That(() => _serializer.Deserialize<SessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Reject_Explicit_Null_For_Optional_Nonnull_Collections()
    {
        var json = _fixtures.LoadJson("Serialization.null-optional-collection.json");

        _ = await Assert.That(() => _serializer.Deserialize<SessionMessage>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Deserialize_Should_Throw_JsonException_When_Union_Payload_Is_Not_An_Object()
    {
        var json = _fixtures.LoadJson("Serialization.non-object-assistant-payload.json");

        _ = await Assert.That(() => _serializer.Deserialize<SessionMessageAssistantContent>(json))
            .Throws<JsonException>();
    }

    [Test]
    public async Task ProviderMetadata_Should_Recursively_Copy_Collections()
    {
        var inner = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["original"] = default,
        };
        var outer = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
        {
            ["provider"] = inner,
        };
        var model = new SessionMessageAssistantReasoning
        {
            ID = "part-1",
            Text = "reasoning",
            ProviderMetadata = outer,
        };

        inner.Add("inner-mutation", default);
        outer.Add("outer-mutation", new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        await Assert.That(model.ProviderMetadata.ContainsKey("outer-mutation")).IsFalse();
        await Assert.That(model.ProviderMetadata["provider"].ContainsKey("inner-mutation")).IsFalse();
    }

    [Test]
    public async Task GetTypeInfo_Should_Resolve_Union_Metadata_Without_Reflection_Fallback()
    {
        await Assert.That(JsonSerializer.IsReflectionEnabledByDefault).IsFalse();
        await Assert.That(_serializer.GetTypeInfo(typeof(SessionMessage))).IsNotNull();
        await Assert.That(_serializer.GetTypeInfo(typeof(SessionMessageAssistantContent))).IsNotNull();
        await Assert.That(_serializer.GetTypeInfo(typeof(SessionMessageToolState))).IsNotNull();
    }
}
