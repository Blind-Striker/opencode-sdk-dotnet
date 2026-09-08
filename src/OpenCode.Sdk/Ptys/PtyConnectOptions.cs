using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk;

/// <summary>
/// Shapes one PTY WebSocket connection and its local send budget. The location and cursor ride
/// the upgrade query; the send timeout stays in the SDK.
/// </summary>
public sealed record PtyConnectOptions
{
    private readonly TerminalSendTimeout _sendTimeout = TerminalSendTimeout.Default;

    /// <summary>
    /// Gets the fixed total budget for one WebSocket send, including its serialization wait.
    /// The default is 30 seconds; this does not limit connection establishment or command execution.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The budget is below one millisecond or above int.MaxValue milliseconds.</exception>
    public TimeSpan SendTimeout
    {
        get => _sendTimeout.Value;
        init => _sendTimeout = new TerminalSendTimeout(value);
    }

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
