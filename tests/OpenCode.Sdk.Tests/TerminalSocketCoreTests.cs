using System.Net.WebSockets;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The shared core's construction contract and the identity its refusals name. Both families'
/// session tests exercise the read, write, and close behaviour through their own doors; what only
/// the core can be asked directly is which collaborator it refuses to run without, and which type
/// a caller sees named when it writes to a session it has already disposed.
/// </summary>
public sealed class TerminalSocketCoreTests
{
    [Test]
    public async Task Constructor_Should_Refuse_A_Null_Socket()
    {
        var failure = Assert.Throws<ArgumentNullException>(() => _ = new TerminalSocketCore<PtyFrame>(
            socket: null!, PtyFrameDecoder.Instance, PtyClosePolicy.Instance, typeof(PtySession)));

        await Assert.That(failure.ParamName).IsEqualTo("socket");
    }

    [Test]
    public async Task Constructor_Should_Refuse_A_Null_Decoder()
    {
        using var socket = new ScriptedTerminalWebSocket();

        var failure = Assert.Throws<ArgumentNullException>(() => _ = new TerminalSocketCore<PtyFrame>(
            socket, decoder: null!, PtyClosePolicy.Instance, typeof(PtySession)));

        await Assert.That(failure.ParamName).IsEqualTo("decoder");
    }

    [Test]
    public async Task Constructor_Should_Refuse_A_Null_Close_Policy()
    {
        using var socket = new ScriptedTerminalWebSocket();

        var failure = Assert.Throws<ArgumentNullException>(() => _ = new TerminalSocketCore<PtyFrame>(
            socket, PtyFrameDecoder.Instance, closePolicy: null!, typeof(PtySession)));

        await Assert.That(failure.ParamName).IsEqualTo("closePolicy");
    }

    [Test]
    public async Task Constructor_Should_Refuse_A_Null_Owner()
    {
        using var socket = new ScriptedTerminalWebSocket();

        var failure = Assert.Throws<ArgumentNullException>(() => _ = new TerminalSocketCore<PtyFrame>(
            socket, PtyFrameDecoder.Instance, PtyClosePolicy.Instance, owner: null!));

        await Assert.That(failure.ParamName).IsEqualTo("owner");
    }

    [Test]
    public async Task SendAsync_Should_Name_The_Owning_Door_Once_Disposed()
    {
        using var socket = new ScriptedTerminalWebSocket();
        await using var core = new TerminalSocketCore<PtyFrame>(
            socket, PtyFrameDecoder.Instance, PtyClosePolicy.Instance, typeof(PtySession));
        await core.DisposeAsync();

        var failure = await Assert
            .That(async () => await core.SendAsync(
                new ArraySegment<byte>([0x61]), WebSocketMessageType.Text, CancellationToken.None))
            .Throws<ObjectDisposedException>();

        // The name is deliberately the door the caller disposed, not this internal core: a
        // consumer holds a PtySession and never learns that a TerminalSocketCore exists.
        await Assert.That(failure!.ObjectName).IsEqualTo(typeof(PtySession).FullName);
    }
}
