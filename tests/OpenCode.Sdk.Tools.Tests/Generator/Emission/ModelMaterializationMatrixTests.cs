using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;
using static OpenCode.Sdk.Tools.Tests.Support.BindingScenarioData;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class ModelMaterializationMatrixTests
{
    [Test]
    public async Task Bind_Should_Map_Required_And_Nullable_Axes_Independently()
    {
        var plan = await CreatePlanAsync();
        var model = plan.Models.OfType<ObjectModelPlan>()
            .Single(static candidate => candidate.Name == "MaterializationMatrix");

        foreach (var expected in new[]
                 {
                     ("requiredScalar", true, false),
                     ("requiredNullableScalar", true, true),
                     ("optionalScalar", false, true),
                     ("optionalNullableScalar", false, true),
                     ("requiredNumber", true, false),
                     ("requiredNullableNumber", true, true),
                     ("optionalNumber", false, true),
                     ("optionalNullableNumber", false, true),
                     ("requiredList", true, false),
                     ("requiredNullableList", true, true),
                     ("optionalList", false, true),
                     ("optionalNullableList", false, true),
                     ("requiredDictionary", true, false),
                     ("requiredNullableDictionary", true, true),
                     ("optionalDictionary", false, true),
                     ("optionalNullableDictionary", false, true),
                     ("openKnown", true, false),
                     ("requiredNullableAny", true, false),
                     ("optionalAny", false, true),
                 })
        {
            var property = model.Properties.Single(property => property.WireName == expected.Item1);
            await Assert.That(property.IsRequired).IsEqualTo(expected.Item2);
            await Assert.That(property.Type.IsNullable).IsEqualTo(expected.Item3);
        }

        var nonnullItems = (ListTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "nonnullItems").Type;
        var nullableItems = (ListTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "nullableItems").Type;
        var nonnullValues = (DictionaryTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "nonnullValues").Type;
        var nullableValues = (DictionaryTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "nullableValues").Type;
        var anyItems = (ListTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "anyItems").Type;
        var nullableAnyItems = (ListTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "nullableAnyItems").Type;
        var anyValues = (DictionaryTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "anyValues").Type;
        var nullableAnyValues = (DictionaryTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "nullableAnyValues").Type;
        var optionalAny = (NamedTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "optionalAny").Type;
        await Assert.That(nonnullItems.ElementType.IsNullable).IsFalse();
        await Assert.That(nullableItems.ElementType.IsNullable).IsTrue();
        await Assert.That(nonnullValues.ValueType.IsNullable).IsFalse();
        await Assert.That(nullableValues.ValueType.IsNullable).IsTrue();
        await Assert.That(anyItems.ElementType.IsNullable).IsFalse();
        await Assert.That(anyItems.ElementType.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        await Assert.That(nullableAnyItems.ElementType.IsNullable).IsFalse();
        await Assert.That(nullableAnyItems.ElementType.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        await Assert.That(anyValues.ValueType.IsNullable).IsFalse();
        await Assert.That(anyValues.ValueType.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        await Assert.That(nullableAnyValues.ValueType.IsNullable).IsFalse();
        await Assert.That(nullableAnyValues.ValueType.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        await Assert.That(optionalAny.IsNullable).IsTrue();
        await Assert.That(optionalAny.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        var requiredAny = (NamedTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "requiredAny").Type;
        var requiredNullableAny = (NamedTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "requiredNullableAny").Type;
        var freeform = (DictionaryTypeReferencePlan)model.Properties
            .Single(static property => property.WireName == "freeform").Type;
        await Assert.That(requiredAny.Name).IsEqualTo("JsonElement");
        await Assert.That(requiredAny.IsNullable).IsFalse();
        await Assert.That(requiredAny.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        await Assert.That(requiredNullableAny.IsNullable).IsFalse();
        await Assert.That(requiredNullableAny.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
        await Assert.That(freeform.ValueType.IsNullable).IsFalse();
        await Assert.That(freeform.ValueType.JsonNullRepresentation).IsEqualTo(JsonNullRepresentation.InBand);
    }

    [Test]
    public async Task Emit_Should_Compile_Source_Generate_And_Materialize_The_Nullability_Matrix()
    {
        var plan = await CreatePlanAsync();
        var assembly = await GeneratedSourceCompiler.CompileAndLoadWithSdkCoreAsync(SourceEmitter.Emit(plan));
        var typeInfo = ResolveTypeInfo(assembly, "OpenCode.Sdk.Models.MaterializationMatrix");

        var value = Deserialize(CreatePayload(), typeInfo);
        await AssertOptionalPropertiesAreNullAsync(value);
        await AssertCollectionChildrenAndLiteralAsync(value);
        await AssertUnknownFieldsAreSkippedAsync(value);
        await AssertSerializationAsync(value, typeInfo);
        await AssertExplicitOptionalNullsAsync(typeInfo);
        await AssertNonNullOptionalAndNullableValuesAsync(typeInfo);
        await AssertRequiredFailuresAsync(typeInfo);
        await AssertRootUnionNullFailsAsync(assembly, plan);
    }

    private static async Task AssertOptionalPropertiesAreNullAsync(object value)
    {
        foreach (var name in new[]
                 {
                     "OptionalScalar",
                     "OptionalNullableScalar",
                     "OptionalNumber",
                     "OptionalNullableNumber",
                     "OptionalList",
                     "OptionalNullableList",
                     "OptionalDictionary",
                     "OptionalNullableDictionary",
                     "OptionalAny",
                 })
        {
            await Assert.That(GetProperty(value, name)).IsNull();
        }
    }

    private static async Task AssertCollectionChildrenAndLiteralAsync(object value)
    {
        await Assert.That(((IList)GetProperty(value, "NonnullItems")!)[0]).IsNull();
        await Assert.That(((IList)GetProperty(value, "NullableItems")!)[0]).IsNull();
        await Assert.That(((IDictionary)GetProperty(value, "NonnullValues")!)["key"]).IsNull();
        await Assert.That(((IDictionary)GetProperty(value, "NullableValues")!)["key"]).IsNull();
        await Assert.That(((IList)GetProperty(value, "Choices")!)[0]).IsNull();
        var requiredAny = (JsonElement)GetProperty(value, "RequiredAny")!;
        var requiredNullableAny = (JsonElement)GetProperty(value, "RequiredNullableAny")!;
        var anyItem = (JsonElement)((IList)GetProperty(value, "AnyItems")!)[0]!;
        var nullableAnyItem = (JsonElement)((IList)GetProperty(value, "NullableAnyItems")!)[0]!;
        var anyValue = (JsonElement)((IDictionary)GetProperty(value, "AnyValues")!)["key"]!;
        var nullableAnyValue = (JsonElement)((IDictionary)GetProperty(value, "NullableAnyValues")!)["key"]!;
        var freeformValue = (JsonElement)((IDictionary)GetProperty(value, "Freeform")!)["key"]!;
        await Assert.That(requiredAny.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(requiredNullableAny.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(anyItem.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(nullableAnyItem.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(anyValue.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(nullableAnyValue.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(freeformValue.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That((bool)GetProperty(value, "FixedFlag")!).IsFalse();
    }

    private static async Task AssertUnknownFieldsAreSkippedAsync(object value)
    {
        await Assert.That(GetProperty(value, "RequiredScalar")).IsEqualTo("value");
        var openKnown = GetProperty(value, "OpenKnown")
                        ?? throw new InvalidOperationException("The open known object deserialized to null.");
        await Assert.That(GetProperty(openKnown, "Value")).IsEqualTo("known");
    }

    private static async Task AssertSerializationAsync(object value, JsonTypeInfo typeInfo)
    {
        var serialized = await SerializeAsync(value, typeInfo);
        using (var document = JsonDocument.Parse(serialized))
        {
            foreach (var name in new[]
                     {
                         "requiredNullableScalar",
                         "requiredNullableNumber",
                         "requiredNullableList",
                         "requiredNullableDictionary",
                         "requiredAny",
                         "requiredNullableAny",
                     })
            {
                await Assert.That(document.RootElement.GetProperty(name).ValueKind).IsEqualTo(JsonValueKind.Null);
            }

            await Assert.That(document.RootElement.TryGetProperty("optionalScalar", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalList", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalDictionary", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalAny", out _)).IsFalse();
            await Assert.That(document.RootElement.GetProperty("anyItems")[0].ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(document.RootElement.GetProperty("nullableAnyItems")[0].ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(document.RootElement.GetProperty("anyValues").GetProperty("key").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
            await Assert.That(document.RootElement.GetProperty("nullableAnyValues").GetProperty("key").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
            await Assert.That(document.RootElement.GetProperty("freeform").GetProperty("key").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
            await Assert.That(document.RootElement.GetProperty("fixedFlag").GetBoolean()).IsFalse();
        }
    }

    private static async Task AssertExplicitOptionalNullsAsync(JsonTypeInfo typeInfo)
    {
        var explicitOptionalNulls = CreatePayload();
        foreach (var name in new[]
                 {
                     "optionalScalar",
                     "optionalNullableScalar",
                     "optionalNumber",
                     "optionalNullableNumber",
                     "optionalList",
                     "optionalNullableList",
                     "optionalDictionary",
                     "optionalNullableDictionary",
                     "optionalAny",
                 })
        {
            explicitOptionalNulls[name] = null;
        }

        var explicitNullValue = Deserialize(explicitOptionalNulls, typeInfo);
        var explicitNullJson = await SerializeAsync(explicitNullValue, typeInfo);
        using (var document = JsonDocument.Parse(explicitNullJson))
        {
            await Assert.That(document.RootElement.TryGetProperty("optionalNullableScalar", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalNullableNumber", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalNullableList", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalNullableDictionary", out _)).IsFalse();
            await Assert.That(document.RootElement.TryGetProperty("optionalAny", out _)).IsFalse();
        }
    }

    private static async Task AssertNonNullOptionalAndNullableValuesAsync(JsonTypeInfo typeInfo)
    {
        var payload = CreatePayload();
        payload["requiredNullableScalar"] = "required";
        payload["requiredNullableNumber"] = 1.5;
        payload["requiredNullableList"] = new JsonArray(JsonValue.Create("required"));
        payload["requiredNullableDictionary"] = new JsonObject { ["key"] = "required", };
        payload["optionalScalar"] = "optional";
        payload["optionalNullableScalar"] = "optional-nullable";
        payload["optionalNumber"] = 2.5;
        payload["optionalNullableNumber"] = 3.5;
        payload["optionalList"] = new JsonArray(JsonValue.Create("optional"));
        payload["optionalNullableList"] = new JsonArray(JsonValue.Create("optional-nullable"));
        payload["optionalDictionary"] = new JsonObject { ["key"] = "optional", };
        payload["optionalNullableDictionary"] = new JsonObject { ["key"] = "optional-nullable", };
        payload["optionalAny"] = new JsonObject { ["present"] = true, };

        var value = Deserialize(payload, typeInfo);
        var serialized = await SerializeAsync(value, typeInfo);
        using var document = JsonDocument.Parse(serialized);
        foreach (var name in new[]
                 {
                     "requiredNullableScalar",
                     "requiredNullableNumber",
                     "requiredNullableList",
                     "requiredNullableDictionary",
                     "optionalScalar",
                     "optionalNullableScalar",
                     "optionalNumber",
                     "optionalNullableNumber",
                     "optionalList",
                     "optionalNullableList",
                     "optionalDictionary",
                     "optionalNullableDictionary",
                     "optionalAny",
                 })
        {
            await Assert.That(document.RootElement.TryGetProperty(name, out _)).IsTrue();
        }

        await Assert.That(document.RootElement.GetProperty("requiredNullableScalar").GetString()).IsEqualTo("required");
        await Assert.That(document.RootElement.GetProperty("requiredNullableNumber").GetDouble()).IsEqualTo(1.5);
        await Assert.That(document.RootElement.GetProperty("requiredNullableList")[0].GetString()).IsEqualTo("required");
        await Assert.That(document.RootElement.GetProperty("requiredNullableDictionary").GetProperty("key").GetString())
            .IsEqualTo("required");
        await Assert.That(document.RootElement.GetProperty("optionalNumber").GetDouble()).IsEqualTo(2.5);
        await Assert.That(document.RootElement.GetProperty("optionalNullableNumber").GetDouble()).IsEqualTo(3.5);
    }

    private static async Task AssertRequiredFailuresAsync(JsonTypeInfo typeInfo)
    {
        foreach (var name in new[]
                 {
                     "requiredScalar",
                     "requiredNullableScalar",
                     "requiredNumber",
                     "requiredNullableNumber",
                     "requiredList",
                     "requiredNullableList",
                     "requiredDictionary",
                     "requiredNullableDictionary",
                     "requiredChoice",
                     "choices",
                     "fixedFlag",
                     "requiredAny",
                     "requiredNullableAny",
                     "anyItems",
                     "nullableAnyItems",
                     "anyValues",
                     "nullableAnyValues",
                     "freeform",
                     "openKnown",
                 })
        {
            var missing = CreatePayload();
            _ = missing.Remove(name);
            _ = await Assert.That(() => JsonSerializer.Deserialize(missing.ToJsonString(), typeInfo)).Throws<JsonException>();
        }

        foreach (var name in new[]
                 {
                     "requiredScalar",
                     "requiredNumber",
                     "requiredList",
                     "requiredDictionary",
                     "requiredChoice",
                     "choices",
                     "fixedFlag",
                     "anyItems",
                     "nullableAnyItems",
                     "anyValues",
                     "nullableAnyValues",
                     "freeform",
                     "openKnown",
                 })
        {
            var explicitNull = CreatePayload();
            explicitNull[name] = null;
            _ = await Assert.That(() => JsonSerializer.Deserialize(explicitNull.ToJsonString(), typeInfo)).Throws<JsonException>();
        }
    }

    private static async Task<EmitPlan> CreatePlanAsync()
    {
        var document = await BindingTestHost.IngestAsync(new MaterializationMatrixScenario());
        return new BindingTestHost().Bind(
            document,
            Selection(MaterializationMatrixScenario.GetOperationId, MaterializationMatrixScenario.ChoiceOperationId),
            Curation(Groups("matrix", RootGroup())));
    }

    private static JsonObject CreatePayload() => new()
    {
        ["requiredScalar"] = "value",
        ["requiredNullableScalar"] = null,
        ["requiredNumber"] = 1.0,
        ["requiredNullableNumber"] = null,
        ["requiredList"] = new JsonArray(JsonValue.Create("value")),
        ["requiredNullableList"] = null,
        ["requiredDictionary"] = new JsonObject { ["key"] = "value", },
        ["requiredNullableDictionary"] = null,
        ["nonnullItems"] = new JsonArray((JsonNode?)null),
        ["nullableItems"] = new JsonArray((JsonNode?)null),
        ["nonnullValues"] = new JsonObject { ["key"] = null, },
        ["nullableValues"] = new JsonObject { ["key"] = null, },
        ["requiredAny"] = null,
        ["requiredNullableAny"] = null,
        ["anyItems"] = new JsonArray((JsonNode?)null),
        ["nullableAnyItems"] = new JsonArray((JsonNode?)null),
        ["anyValues"] = new JsonObject { ["key"] = null, },
        ["nullableAnyValues"] = new JsonObject { ["key"] = null, },
        ["freeform"] = new JsonObject { ["key"] = null, },
        ["openKnown"] = new JsonObject { ["value"] = "known", ["unexpectedOpen"] = true, },
        ["requiredChoice"] = new JsonObject { ["type"] = "alpha", },
        ["choices"] = new JsonArray((JsonNode?)null),
        ["fixedFlag"] = false,
        ["unexpectedClosed"] = true,
    };

    private static object Deserialize(JsonObject payload, JsonTypeInfo typeInfo) =>
        JsonSerializer.Deserialize(payload.ToJsonString(), typeInfo)
        ?? throw new InvalidOperationException("The matrix payload deserialized to null.");

    private static JsonTypeInfo ResolveTypeInfo(Assembly assembly, string typeName)
    {
        var contextType = assembly.GetType("OpenCode.Sdk.Internal.Serialization.OpenCodeJsonContext", throwOnError: true)!;
        var context = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as JsonSerializerContext
                      ?? throw new InvalidOperationException("The generated JSON context has no Default instance.");
        var type = assembly.GetType(typeName, throwOnError: true)!;
        return context.GetTypeInfo(type)
               ?? throw new InvalidOperationException($"The generated JSON context has no metadata for '{typeName}'.");
    }

    private static object? GetProperty(object value, string name)
    {
        var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                       ?? throw new InvalidOperationException($"The matrix type has no '{name}' property.");
        return property.GetValue(value);
    }

    private static async Task<string> SerializeAsync(object value, JsonTypeInfo typeInfo)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, typeInfo);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task AssertRootUnionNullFailsAsync(Assembly assembly, EmitPlan plan)
    {
        var operation = plan.Clients.SelectMany(static client => client.Operations)
            .Single(static candidate => candidate.Envelope?.PayloadTypeName == "IMatrixChoice");
        var adapterType = assembly.GetType(
            $"OpenCode.Sdk.Internal.ResponseAdapters.{operation.Envelope!.AdapterTypeName}",
            throwOnError: true)!;
        var adapter = adapterType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                      ?? throw new InvalidOperationException("The generated response adapter has no Instance.");
        var adapt = adapterType.GetMethod("Adapt", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("The generated response adapter has no Adapt method.");

        var exception = Assert.Throws<TargetInvocationException>(() => _ = adapt.Invoke(adapter, [200, "null"]));

        await Assert.That(exception.InnerException?.GetType().FullName).IsEqualTo("OpenCode.Sdk.OpenCodeTransportException");
    }
}
