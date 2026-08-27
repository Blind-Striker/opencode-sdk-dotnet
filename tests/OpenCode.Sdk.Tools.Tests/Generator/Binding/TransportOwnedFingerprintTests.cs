using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class TransportOwnedFingerprintTests
{
    [Test]
    public async Task ComputeSha256_Should_Produce_A_Lowercase_Hex_Digest()
    {
        var document = await BindingTestHost.IngestAsync(Scenario());
        var operation = document.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");

        var hash = TransportOwnedFingerprint.ComputeSha256(operation);

        await Assert.That(hash.Length).IsEqualTo(64);
        await Assert.That(hash.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'))).IsTrue();
    }

    /// <summary>Query-parameter declaration order carries no wire meaning, so an upstream
    /// byte-shuffle that only reorders the array must not change the fingerprint.</summary>
    [Test]
    public async Task ComputeSha256_Should_Ignore_Declared_Parameter_Order()
    {
        var forward = await BindingTestHost.IngestAsync(Scenario());
        var reordered = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.pty.connect", path: "/api/pty/{ptyID}/connect", configure: operation => operation
                .Parameter("ticket", "query", schema => schema.Type("string"))
                .Parameter("cursor", "query", schema => schema.Type("string"))
                .Parameter("location[workspace]", "query", schema => schema.Type("string"))
                .Parameter("location[directory]", "query", schema => schema.Type("string"))
                .Parameter("ptyID", "path", schema => schema.Type("string"), required: true)
                .Extension("x-websocket", "true"))));

        var forwardOperation = forward.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");
        var reorderedOperation = reordered.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");

        await Assert
            .That(TransportOwnedFingerprint.ComputeSha256(reorderedOperation))
            .IsEqualTo(TransportOwnedFingerprint.ComputeSha256(forwardOperation));
    }

    [Test]
    public async Task ComputeSha256_Should_Change_When_The_WebSocket_Marker_Is_Missing()
    {
        var withMarker = await BindingTestHost.IngestAsync(Scenario());
        var withoutMarker = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.pty.connect", path: "/api/pty/{ptyID}/connect", configure: operation => operation
                .Parameter("ptyID", "path", schema => schema.Type("string"), required: true)
                .Parameter("location[directory]", "query", schema => schema.Type("string"))
                .Parameter("location[workspace]", "query", schema => schema.Type("string"))
                .Parameter("cursor", "query", schema => schema.Type("string"))
                .Parameter("ticket", "query", schema => schema.Type("string")))));

        var withMarkerOperation = withMarker.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");
        var withoutMarkerOperation = withoutMarker.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");

        await Assert
            .That(TransportOwnedFingerprint.ComputeSha256(withoutMarkerOperation))
            .IsNotEqualTo(TransportOwnedFingerprint.ComputeSha256(withMarkerOperation));
    }

    [Test]
    public async Task ComputeSha256_Should_Change_When_A_Parameter_Is_Added()
    {
        var baseline = await BindingTestHost.IngestAsync(Scenario());
        var extended = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.pty.connect", path: "/api/pty/{ptyID}/connect", configure: operation => operation
                .Parameter("ptyID", "path", schema => schema.Type("string"), required: true)
                .Parameter("location[directory]", "query", schema => schema.Type("string"))
                .Parameter("location[workspace]", "query", schema => schema.Type("string"))
                .Parameter("cursor", "query", schema => schema.Type("string"))
                .Parameter("ticket", "query", schema => schema.Type("string"))
                .Parameter("extra", "query", schema => schema.Type("string"))
                .Extension("x-websocket", "true"))));

        var baselineOperation = baseline.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");
        var extendedOperation = extended.Operations.Single(static candidate => candidate.OperationId == "v2.pty.connect");

        await Assert
            .That(TransportOwnedFingerprint.ComputeSha256(extendedOperation))
            .IsNotEqualTo(TransportOwnedFingerprint.ComputeSha256(baselineOperation));
    }

    private static SpecScenario Scenario() =>
        SpecScenario.Define(spec => spec
            .WithOperation("v2.pty.connect", path: "/api/pty/{ptyID}/connect", configure: operation => operation
                .Parameter("ptyID", "path", schema => schema.Type("string"), required: true)
                .Parameter("location[directory]", "query", schema => schema.Type("string"))
                .Parameter("location[workspace]", "query", schema => schema.Type("string"))
                .Parameter("cursor", "query", schema => schema.Type("string"))
                .Parameter("ticket", "query", schema => schema.Type("string"))
                .Extension("x-websocket", "true")));
}
