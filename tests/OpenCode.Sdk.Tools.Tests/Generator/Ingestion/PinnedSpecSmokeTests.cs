using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

/// <summary>
/// The structural gate every future spec refresh runs through: representative landmarks at
/// the known lossy seams, no count assertions. A red landmark is evidence about the wire —
/// stop and classify per the deviation protocol, never patch the assertion.
/// </summary>
public sealed class PinnedSpecSmokeTests
{
    [Test]
    public async Task Ingest_Should_Absorb_The_Pinned_Spec_With_Both_Surfaces()
    {
        var document = await IngestPinnedSpecAsync();

        await Assert.That(document.Operations.Any(static operation => operation.Surface == SpecSurface.Modern)).IsTrue();
        await Assert.That(document.Operations.Any(static operation => operation.Surface == SpecSurface.Legacy)).IsTrue();
    }

    [Test]
    public async Task Ingest_Should_Flag_Stream_Wildcard_And_WebSocket_Operations()
    {
        var document = await IngestPinnedSpecAsync();

        var durableStream = document.Operations.Single(static operation => operation.OperationId == "v2.session.events");
        await Assert.That(durableStream.IsSse).IsTrue();
        await Assert.That(durableStream.Responses.Single(static response => response.IsSse).EffectStreamJson).IsNotNull();
        await Assert.That(document.Operations.Single(static operation => operation.OperationId == "v2.fs.read").HasWildcardPath).IsTrue();
        await Assert.That(document.Operations.Single(static operation => operation.OperationId == "v2.pty.connect").IsWebSocket).IsTrue();
    }

    [Test]
    public async Task Ingest_Should_Classify_Marked_And_Structural_Union_Landmarks()
    {
        var document = await IngestPinnedSpecAsync();

        var durableEvent = (UnionNode)document.Schemas["SessionDurableEvent"];
        await Assert.That(durableEvent.Keyword).IsEqualTo(UnionKeyword.OneOf);
        await Assert.That(durableEvent.Classification).IsEqualTo(UnionClassification.Marked);

        var formatter = (UnionNode)document.Schemas["Config#/properties/formatter"];
        await Assert.That(formatter.Classification).IsEqualTo(UnionClassification.Structural);

        var pluginItems = (UnionNode)document.Schemas["Config#/properties/plugin/items"];
        await Assert.That(pluginItems.Branches.OfType<TupleNode>().Single().Items).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Ingest_Should_Project_Special_Number_And_Unrestricted_Landmarks()
    {
        var document = await IngestPinnedSpecAsync();

        var workspace = (ObjectNode)document.Schemas["Workspace"];
        await Assert.That(workspace.Properties.Single(static property => property.Name == "timeUsed").Schema)
            .IsTypeOf<SpecialNumberNode>();

        var assistantMessage = (ObjectNode)document.Schemas["AssistantMessage"];
        await Assert.That(assistantMessage.Properties.Single(static property => property.Name == "structured").Schema)
            .IsTypeOf<UnrestrictedNode>();
    }

    [Test]
    public async Task Ingest_Should_Classify_Both_Error_Styles()
    {
        var document = await IngestPinnedSpecAsync();

        await Assert.That(((ObjectNode)document.Schemas["MoveSessionError"]).ErrorStyle).IsEqualTo(ErrorStyle.NameData);
        await Assert.That(((ObjectNode)document.Schemas["effect_HttpApiError_BadRequest"]).ErrorStyle).IsEqualTo(ErrorStyle.EffectTag);
    }

    [Test]
    public async Task Ingest_Should_Classify_Envelope_Shapes()
    {
        var document = await IngestPinnedSpecAsync();

        await Assert.That(EnvelopeOf(document, "v2.session.list")).IsEqualTo(SpecEnvelopeShape.CursorData);
        await Assert.That(EnvelopeOf(document, "v2.session.history")).IsEqualTo(SpecEnvelopeShape.DataHasMore);
        await Assert.That(EnvelopeOf(document, "v2.agent.list")).IsEqualTo(SpecEnvelopeShape.DataLocation);
    }

    [Test]
    public async Task Ingest_Should_Be_Deterministic_Across_Repeated_Runs()
    {
        var first = await IngestPinnedSpecAsync();
        var second = await IngestPinnedSpecAsync();

        await Assert.That(first.Schemas.Keys.SequenceEqual(second.Schemas.Keys, StringComparer.Ordinal)).IsTrue();
        await Assert.That(first.Operations.Select(static operation => operation.OperationId)
            .SequenceEqual(second.Operations.Select(static operation => operation.OperationId), StringComparer.Ordinal)).IsTrue();
    }

    private static SpecEnvelopeShape EnvelopeOf(SpecDocument document, string operationId) =>
        document.Operations.Single(operation => operation.OperationId == operationId)
            .Responses.Single(static response => response.StatusCode == 200)
            .EnvelopeShape;

    private static Task<SpecDocument> IngestPinnedSpecAsync()
    {
        var fileSystem = new RealFileSystem();
        var specPath = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi.json");
        return new SpecIngestion(fileSystem).IngestAsync(specPath, CancellationToken.None);
    }
}
