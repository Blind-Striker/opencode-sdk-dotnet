namespace OpenCode.Sdk;

/// <summary>
/// Shapes one PTY WebSocket connection. Both members ride the upgrade URL's query, so the scope
/// they resolve must agree with the scope the token door resolved.
/// </summary>
public sealed record PtyConnectOptions
{
    /// <summary>
    /// The largest cursor the server accepts: it validates against the JavaScript safe-integer
    /// range and silently ignores anything outside it, which would turn a resume into a full
    /// replay. The SDK refuses the value instead.
    /// </summary>
    private const long MaximumCursor = 9_007_199_254_740_991;

    /// <summary>The smallest cursor the server accepts; it means "attach live, replay nothing".</summary>
    private const long MinimumCursor = -1;

    /// <summary>Spelled out rather than interpolated: the bounds are fixed and culture-free.</summary>
    private const string CursorRangeFailure =
        "The PTY cursor must be null for a full replay, or between -1 and 9007199254740991.";

    private readonly long? _cursor;

    /// <summary>
    /// Gets the replay position: null replays the full retained buffer, <c>-1</c> attaches
    /// live-only, and a value greater than or equal to zero resumes from that absolute output
    /// cursor. A value outside the server's accepted range is refused.
    /// </summary>
    public long? Cursor
    {
        get => _cursor;
        init
        {
            if (value is { } cursor && cursor is < MinimumCursor or > MaximumCursor)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, CursorRangeFailure);
            }

            _cursor = value;
        }
    }

    /// <summary>
    /// Gets the per-call location; unset members inherit the client's ambient location member by
    /// member. The connect scope must agree with the scope the token door resolved.
    /// </summary>
    public LocationSelector? Location { get; init; }
}
