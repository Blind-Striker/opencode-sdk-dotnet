using OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

public sealed class GraphKeyBuilderTests
{
    [Test]
    public async Task Append_Should_Escape_Slash_In_Segment()
    {
        var keys = new GraphKeyBuilder();

        var pointer = keys.Append(string.Empty, "a/b");

        await Assert.That(pointer).IsEqualTo("/a~1b");
    }

    [Test]
    public async Task Append_Should_Escape_Tilde_In_Segment()
    {
        var keys = new GraphKeyBuilder();

        var pointer = keys.Append(string.Empty, "a~b");

        await Assert.That(pointer).IsEqualTo("/a~0b");
    }
}
