using OpenCode.Sdk.Tools.Generator.Parsing.Operations;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SpecMediaTypeTests
{
    [Test]
    public async Task Create_Should_Strip_Parameters()
    {
        var media = SpecMediaType.Create("text/x-diff; charset=utf-8");

        await Assert.That(media.Raw).IsEqualTo("text/x-diff; charset=utf-8");
        await Assert.That(media.Stripped).IsEqualTo("text/x-diff");
        await Assert.That(media.IsJson).IsFalse();
        await Assert.That(media.IsEventStream).IsFalse();
    }

    [Test]
    public async Task Create_Should_Detect_Json()
    {
        await Assert.That(SpecMediaType.Create("application/json").IsJson).IsTrue();
    }

    [Test]
    public async Task Create_Should_Detect_Json_Suffix()
    {
        await Assert.That(SpecMediaType.Create("application/problem+json").IsJson).IsTrue();
    }

    [Test]
    public async Task Create_Should_Detect_Event_Stream()
    {
        await Assert.That(SpecMediaType.Create("text/event-stream").IsEventStream).IsTrue();
    }

    [Test]
    public async Task Create_Should_Lowercase_Stripped_Value()
    {
        await Assert
            .That(SpecMediaType.Create("Application/JSON").Stripped)
            .IsEqualTo("application/json");
    }

    [Test]
    public async Task Create_Should_Throw_When_Media_Type_Is_Malformed()
    {
        await Assert.That(() => SpecMediaType.Create("no-slash")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Create_Should_Throw_When_Media_Type_Is_Blank()
    {
        await Assert.That(() => SpecMediaType.Create(" ")).Throws<ArgumentException>();
    }
}
