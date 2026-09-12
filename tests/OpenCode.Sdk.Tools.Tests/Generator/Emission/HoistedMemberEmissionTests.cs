using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class HoistedMemberEmissionTests
{
    [Test]
    public async Task Emit_Should_Render_The_Hoisted_Carrier_And_Its_Implementations()
    {
        var sources = EmitterSnapshot.Create(SourceEmitter.Emit(EmitterPlanFixture.CreateHoistPlan()));

        await Verify(sources);
    }

    [Test]
    public async Task Emit_Should_Declare_The_Hoisted_Carrier_As_A_Plain_Interface()
    {
        var carrier = HoistedSource("Models/IDurableEnvelope.cs");

        await Assert.That(carrier).Contains("public interface IDurableEnvelope");
        await Assert.That(carrier).Contains("public long Seq { get; }");
        await Assert.That(carrier).Contains("public double Version { get; }");
        // The carrier has no converter and no unknown arm: nothing deserializes into it.
        await Assert.That(carrier).DoesNotContain("JsonConverter");
    }

    [Test]
    public async Task Emit_Should_Declare_Every_Hoisted_Union_Member_Nullable_And_Say_Why()
    {
        var union = HoistedSource("Models/IExampleEvent.cs");

        await Assert.That(union).Contains("public IDurableEnvelope? Durable { get; }");
        await Assert.That(union).Contains("public double? Created { get; }");
        await Assert.That(union).Contains("public string? Id { get; }");
        await Assert.That(union).Contains("null when the payload is an unrecognized variant preserved as");
        await Assert.That(union).Contains("UnknownExampleEvent");
    }

    [Test]
    public async Task Emit_Should_Make_The_Unknown_Carrier_Answer_Null_For_Every_Hoisted_Member_In_Its_Chain()
    {
        var carrier = HoistedSource("Models/UnknownExamplePhase.cs");

        await Assert.That(carrier).Contains("IDurableEnvelope? IExampleEvent.Durable => null;");
        await Assert.That(carrier).Contains("double? IExampleEvent.Created => null;");
        await Assert.That(carrier).Contains("string? IExampleEvent.Id => null;");
        await Assert.That(carrier).Contains("string? IExamplePhase.Phase => null;");
    }

    [Test]
    public async Task Emit_Should_Keep_A_Variant_Property_Concrete_And_Answer_The_Interface_Explicitly()
    {
        var variant = HoistedSource("Models/CreatedEvent.cs");

        await Assert.That(variant).Contains("public required CreatedEventDurable Durable { get; init; }");
        await Assert.That(variant).Contains("IDurableEnvelope? IExampleEvent.Durable => Durable;");
        await Assert.That(variant).Contains("double? IExampleEvent.Created => Created;");
        // A reference type already answers the nullable annotation, so no explicit member appears.
        await Assert.That(variant).DoesNotContain("string? IExampleEvent.Id");
    }

    [Test]
    public async Task Emit_Should_Let_A_Promoted_Record_Implement_The_Carrier_Beside_Its_Own_Identity()
    {
        var promoted = HoistedSource("Models/CreatedEventDurable.cs");

        await Assert.That(promoted).Contains("public sealed record CreatedEventDurable : IDurableEnvelope");
        await Assert.That(promoted).Contains("public required long Seq { get; init; }");
    }

    [Test]
    public async Task Emit_Should_Keep_The_Hoisted_Carrier_Out_Of_The_Serializer_Registry()
    {
        var registry = HoistedSource("Internal/Serialization/OpenCodeJsonContext.cs");

        await Assert.That(registry).DoesNotContain("IDurableEnvelope");
        await Assert.That(registry).Contains("CreatedEventDurable");
    }

    private static string HoistedSource(string relativePath) =>
        EmitterSnapshot.Content(SourceEmitter.Emit(EmitterPlanFixture.CreateHoistPlan()), relativePath);
}
