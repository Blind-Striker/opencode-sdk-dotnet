namespace OpenCode.Sdk;

/// <summary>
/// One message read from a live PTY WebSocket. The wire carries exactly two shapes — terminal
/// output and the replay-cursor control frame — so this hierarchy is closed: only
/// <see cref="PtyOutputFrame"/> and <see cref="PtyCursorFrame"/> derive from it.
/// </summary>
public abstract class PtyFrame
{
    private protected PtyFrame()
    {
    }
}
