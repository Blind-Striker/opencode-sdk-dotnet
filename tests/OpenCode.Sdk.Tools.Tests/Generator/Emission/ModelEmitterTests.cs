using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class ModelEmitterTests
{
    [Test]
    public async Task Emit_Should_Produce_Immutable_Annotated_Models()
    {
        var sources = ModelEmitter.Emit(EmitterPlanFixture.CreateModelSnapshot());

        await Verify(EmitterSnapshot.Create(sources));
    }

    [Test]
    public async Task Emit_Should_Use_Shallow_Init_Only_Collection_References()
    {
        var source = EmitterSnapshot.Create(ModelEmitter.Emit(EmitterPlanFixture.CreateModelSnapshot()));

        await Assert.That(source).Contains("public IReadOnlyList<string>? Tags { get; init; }");
        await Assert.That(source).Contains("public IReadOnlyDictionary<string, Uri>? Links { get; init; }");
        await Assert.That(source).Contains("public required IReadOnlyList<string> RequiredTags { get; init; }");
        await Assert.That(source).DoesNotContain("OptionalCollectionInput");
        await Assert.That(source).DoesNotContain("new ReadOnlyDictionary");
        await Assert.That(source).DoesNotContain("new List");
    }

    [Test]
    public async Task Emit_Should_Write_Null_Only_For_Required_Nullable_Properties()
    {
        var source = EmitterSnapshot.Create(ModelEmitter.Emit(EmitterPlanFixture.CreateModelSnapshot()));

        await Assert.That(source).Contains("[JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]");
        await Assert.That(source).Contains("public required string? RequiredNullable { get; init; }");
        await Assert.That(source).DoesNotContain("WireNullRejecting");
    }
}
