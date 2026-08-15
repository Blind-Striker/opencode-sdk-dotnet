using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

/// <summary>
/// The structural gate every future spec refresh runs through: representative landmarks at
/// the known lossy seams, no count assertions. A red landmark is evidence about the wire,
/// never a reason to patch the assertion.
/// </summary>
public sealed class PinnedSpecSmokeTests
{
    [Test]
    public async Task Ingest_Should_Absorb_The_Pinned_Spec()
    {
        var document = await IngestPinnedSpecAsync();

        await Assert.That(document.Operations).IsNotEmpty();
        await Assert.That(document.Operations.All(static operation =>
            operation.OperationId.StartsWith("v2.", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Ingest_Should_Flag_Stream_Wildcard_And_WebSocket_Operations()
    {
        var document = await IngestPinnedSpecAsync();

        var sessionLog = document.Operations.Single(static operation => operation.OperationId == "v2.session.log");
        await Assert.That(sessionLog.IsSse).IsTrue();
        await Assert.That(sessionLog.Responses.Single(static response => response.IsSse).EffectStreamJson).IsNotNull();
        await Assert.That(document.Operations.Single(static operation => operation.OperationId == "v2.event.subscribe").IsSse)
            .IsTrue();
        await Assert.That(document.Operations.Single(static operation => operation.OperationId == "v2.fs.read").HasWildcardPath)
            .IsTrue();
        await Assert.That(document.Operations.Single(static operation => operation.OperationId == "v2.pty.connect").IsWebSocket)
            .IsTrue();
    }

    [Test]
    public async Task Ingest_Should_Classify_Marked_Nested_And_Structural_Union_Landmarks()
    {
        var document = await IngestPinnedSpecAsync();

        var durableEvent = (UnionNode)document.Schemas["Session.Event.Durable"];
        await Assert.That(durableEvent.Keyword).IsEqualTo(UnionKeyword.OneOf);
        await Assert.That(durableEvent.Classification).IsEqualTo(UnionClassification.Marked);

        var message = (UnionNode)document.Schemas["Session.Message.Info"];
        await Assert.That(message.Classification).IsEqualTo(UnionClassification.Marked);

        var compaction = (UnionNode)document.Schemas["Session.Message.Compaction"];
        await Assert.That(compaction.Classification).IsEqualTo(UnionClassification.Marked);

        var formatter = (UnionNode)document.Schemas["Config.Info#/properties/formatter"];
        await Assert.That(formatter.Classification).IsEqualTo(UnionClassification.Structural);

        var fields = (ArrayNode)document.Schemas["Form.Fields"];
        await Assert.That(fields.Item).IsTypeOf<RefNode>();
        await Assert.That(((RefNode)fields.Item).Target).IsEqualTo("Form.Field");
    }

    [Test]
    public async Task Ingest_Should_Project_Special_Number_And_Unrestricted_Landmarks()
    {
        var document = await IngestPinnedSpecAsync();

        var shell = (ObjectNode)document.Schemas["Session.Message.Shell"];
        await Assert.That(shell.Properties.Single(static property => property.Name == "exit").Schema)
            .IsTypeOf<SpecialNumberNode>();

        var instructionEntry = (ObjectNode)document.Schemas["InstructionEntry.Info"];
        await Assert.That(instructionEntry.Properties.Single(static property => property.Name == "value").Schema)
            .IsTypeOf<UnrestrictedNode>();
    }

    [Test]
    public async Task Ingest_Should_Classify_Both_Error_Styles()
    {
        var document = await IngestPinnedSpecAsync();

        await Assert.That(((ObjectNode)document.Schemas["ProjectCopyError"]).ErrorStyle).IsEqualTo(ErrorStyle.NameData);
        await Assert.That(((ObjectNode)document.Schemas["UnauthorizedError"]).ErrorStyle).IsEqualTo(ErrorStyle.EffectTag);
    }

    [Test]
    public async Task Ingest_Should_Classify_Envelope_Shapes()
    {
        var document = await IngestPinnedSpecAsync();

        await Assert.That(EnvelopeOf(document, "v2.session.list")).IsEqualTo(SpecEnvelopeShape.CursorData);
        await Assert.That(EnvelopeOf(document, "v2.agent.list")).IsEqualTo(SpecEnvelopeShape.DataLocation);
        await Assert.That(EnvelopeOf(document, "v2.session.message")).IsEqualTo(SpecEnvelopeShape.Data);
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
