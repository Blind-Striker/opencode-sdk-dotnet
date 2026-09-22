using System.Text;
using OpenCode.Sdk.Internal.BackgroundService;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// The strict sidecar boundary over embedded fixtures: the valid shape, the null handoff, the
/// absent id, unknown members ignored, and every shape failure read as absent rather than thrown.
/// Round-trip and redaction cover the writer and the secret-bearing ToString.
/// </summary>
public sealed class PtyHandoffSidecarTests
{
    private static readonly FixtureLoader Fixtures = new();

    [Test]
    public async Task TryRead_Should_Decode_A_Valid_Sidecar()
    {
        var sidecar = Decode("BackgroundService.pty-handoff-valid.json");

        await Assert.That(sidecar).IsNotNull();
        await Assert.That(sidecar!.SourceId).IsEqualTo("srv_1");
        await Assert.That(sidecar.SourcePid).IsEqualTo(48213);
        await Assert.That(sidecar.SourceUrl).IsEqualTo("http://127.0.0.1:49374");
        await Assert.That(sidecar.ExpiresAt).IsEqualTo(1700000060000d);
        await Assert.That(sidecar.Handoff).IsNotNull();
        await Assert.That(sidecar.Handoff!.Value.GetProperty("ticket").GetString()).IsEqualTo("ticket-abc123");
    }

    [Test]
    public async Task TryRead_Should_Decode_A_Null_Handoff()
    {
        var sidecar = Decode("BackgroundService.pty-handoff-null-handoff.json");

        await Assert.That(sidecar).IsNotNull();
        await Assert.That(sidecar!.Handoff).IsNull();
    }

    [Test]
    public async Task TryRead_Should_Admit_A_Source_Without_An_Id()
    {
        var sidecar = Decode("BackgroundService.pty-handoff-source-without-id.json");

        await Assert.That(sidecar).IsNotNull();
        await Assert.That(sidecar!.SourceId).IsNull();
        await Assert.That(sidecar.SourcePid).IsEqualTo(48213);
    }

    [Test]
    public async Task TryRead_Should_Ignore_Unknown_Members()
    {
        var sidecar = Decode("BackgroundService.pty-handoff-unknown-members.json");

        await Assert.That(sidecar).IsNotNull();
        await Assert.That(sidecar!.Handoff).IsNotNull();
        await Assert.That(sidecar.Handoff!.Value.GetProperty("ticket").GetString()).IsEqualTo("ticket-abc123");
    }

    [Test]
    [Arguments("BackgroundService.pty-handoff-invalid-handoff.json")]
    [Arguments("BackgroundService.pty-handoff-string-pid.json")]
    [Arguments("BackgroundService.pty-handoff-fractional-pid.json")]
    [Arguments("BackgroundService.pty-handoff-missing-source.json")]
    [Arguments("BackgroundService.pty-handoff-missing-expiry.json")]
    [Arguments("BackgroundService.pty-handoff-nonfinite-expiry.json")]
    [Arguments("BackgroundService.pty-handoff-array-root.json")]
    [Arguments("BackgroundService.pty-handoff-malformed.json")]
    public async Task TryRead_Should_Return_Null_For_A_Shape_Failure(string fixture)
    {
        await Assert.That(Decode(fixture)).IsNull();
    }

    [Test]
    public async Task ToUtf8Json_Should_Round_Trip_A_Sidecar()
    {
        var original = Decode("BackgroundService.pty-handoff-valid.json")!;

        var decoded = PtyHandoffSidecar.TryRead(original.ToUtf8Json());

        await Assert.That(decoded).IsNotNull();
        await Assert.That(decoded!.SourceId).IsEqualTo(original.SourceId);
        await Assert.That(decoded.SourcePid).IsEqualTo(original.SourcePid);
        await Assert.That(decoded.SourceUrl).IsEqualTo(original.SourceUrl);
        await Assert.That(decoded.ExpiresAt).IsEqualTo(original.ExpiresAt);
        await Assert.That(decoded.Handoff!.Value.GetRawText()).IsEqualTo(original.Handoff!.Value.GetRawText());
    }

    [Test]
    public async Task ToUtf8Json_Should_Omit_The_Id_Member_When_The_Source_Has_None()
    {
        var original = Decode("BackgroundService.pty-handoff-null-handoff.json")!;
        var withoutId = original with { SourceId = null };

        var bytes = withoutId.ToUtf8Json();

        await Assert.That(DecodeBytes(bytes)!.SourceId).IsNull();
    }

    [Test]
    public async Task ToString_Should_Not_Render_The_Handoff_Ticket()
    {
        var text = Decode("BackgroundService.pty-handoff-valid.json")!.ToString();

        await Assert.That(text).DoesNotContain("ticket-abc123");
        await Assert.That(text).Contains("<redacted>");
    }

    private static PtyHandoffSidecar? Decode(string fixture) =>
        DecodeBytes(Encoding.UTF8.GetBytes(Fixtures.LoadJson(fixture)));

    private static PtyHandoffSidecar? DecodeBytes(byte[] bytes) => PtyHandoffSidecar.TryRead(bytes);
}
