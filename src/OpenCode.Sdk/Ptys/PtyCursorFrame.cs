namespace OpenCode.Sdk;

/// <summary>
/// The replay boundary: the server sends this once, after the retained buffer has been replayed,
/// carrying the absolute output cursor the replay ended at. A caller that stores it can reconnect
/// with <see cref="PtyConnectOptions.Cursor"/> and resume from exactly there.
/// </summary>
public sealed class PtyCursorFrame : PtyFrame
{
    /// <summary>
    /// Initializes a cursor frame. Public so a consumer substituting <see cref="PtySession"/>
    /// can script the frames its override yields; the SDK's own reader uses the same door.
    /// </summary>
    /// <param name="cursor">The absolute output cursor the replay ended at.</param>
    public PtyCursorFrame(long cursor)
    {
        Cursor = cursor;
    }

    /// <summary>Gets the absolute output cursor the replay ended at.</summary>
    public long Cursor { get; }
}
