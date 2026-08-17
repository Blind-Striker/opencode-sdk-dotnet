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
    public async Task Emit_Should_Route_Optional_Nonnull_Collections_Through_The_Nullable_Input_Helper()
    {
        var source = EmitterSnapshot.Create(ModelEmitter.Emit(EmitterPlanFixture.CreateModelSnapshot()));

        await Assert.That(source).Contains("OptionalCollectionInput.Normalize");
        await Assert.That(source).DoesNotContain("value is null");
    }

    [Test]
    public async Task Emit_Should_Reject_Wire_Null_Only_Where_The_Schema_Forbids_It()
    {
        var source = EmitterSnapshot.Create(ModelEmitter.Emit(EmitterPlanFixture.CreateModelSnapshot()));

        await Assert.That(source).Contains("[JsonConverter(typeof(WireNullRejectingJsonConverter<string>))]");
        await Assert.That(source).Contains("[JsonConverter(typeof(WireNullRejectingValueJsonConverter<double>))]");
        await Assert.That(source).Contains("[JsonConverter(typeof(WireNullRejectingSpecialNumberJsonConverter))]");
        await Assert.That(source).Contains("[JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]");
        var occurrences = source.Split("WireNullRejecting").Length - 1;
        await Assert.That(occurrences).IsEqualTo(4);
    }
}
