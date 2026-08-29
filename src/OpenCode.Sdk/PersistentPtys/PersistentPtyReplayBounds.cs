namespace OpenCode.Sdk;

/// <summary>
/// What the server actually replayed, against what the connection asked for. Knowledge source:
/// upstream-observed — the retained buffer is trimmed as it grows, so a cursor pointing at bytes
/// the terminal no longer holds is silently advanced to the oldest retained byte;
/// <see cref="Truncated"/> is how a caller learns that output between the two offsets is gone.
/// </summary>
/// <param name="RequestedOffset">The output cursor the connect request asked to resume from.</param>
/// <param name="AvailableOffset">The oldest retained output cursor the replay could start at.</param>
/// <param name="EndOffset">The output cursor the replay ends at; the anchor for a later resume.</param>
/// <param name="Truncated">Whether output the request asked for had already been trimmed.</param>
public sealed record PersistentPtyReplayBounds(
    long RequestedOffset,
    long AvailableOffset,
    long EndOffset,
    bool Truncated);
