namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The sandbox's command line: at most one mode flag, and <c>--endpoint &lt;url&gt;</c> for a
/// server you run yourself. Without <c>--endpoint</c> the sandbox connects the way the opencode
/// CLI does (<see cref="SandboxConnection"/>).
/// </summary>
internal sealed record SandboxArguments(SandboxMode Mode, Uri? Endpoint, string? SessionId)
{
    public const string Usage =
        "Usage: [--stream | --events | --paginate <sessionId> | --standalone] [--endpoint <url>]";

    /// <summary>Parses the command line, or returns null when it does not match <see cref="Usage"/>.</summary>
    public static SandboxArguments? Parse(IEnumerable<string> args)
    {
        var pending = new Queue<string>(args);
        SandboxMode? mode = null;
        Uri? endpoint = null;
        string? sessionId = null;

        while (pending.TryDequeue(out var argument))
        {
            switch (argument)
            {
                case "--endpoint" when endpoint is null
                                       && pending.TryDequeue(out var value)
                                       && Uri.TryCreate(value, UriKind.Absolute, out var parsed):
                    endpoint = parsed;
                    break;
                case "--paginate" when mode is null && pending.TryDequeue(out var session):
                    mode = SandboxMode.Paginate;
                    sessionId = session;
                    break;
                case "--stream" when mode is null:
                    mode = SandboxMode.Stream;
                    break;
                case "--events" when mode is null:
                    mode = SandboxMode.Events;
                    break;
                case "--standalone" when mode is null:
                    mode = SandboxMode.Standalone;
                    break;
                default:
                    return null;
            }
        }

        // The standalone demo starts its own server, so an endpoint contradicts it.
        return mode is SandboxMode.Standalone && endpoint is not null
            ? null
            : new SandboxArguments(mode ?? SandboxMode.Walkthrough, endpoint, sessionId);
    }
}
