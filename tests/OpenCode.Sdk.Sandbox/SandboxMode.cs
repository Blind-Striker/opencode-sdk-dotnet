namespace OpenCode.Sdk.Sandbox;

/// <summary>What the sandbox runs.</summary>
internal enum SandboxMode
{
    /// <summary>The standing breadth walkthrough (no mode flag).</summary>
    Walkthrough,

    /// <summary><c>--stream</c>: follow one session's log through the Generic Host.</summary>
    Stream,

    /// <summary><c>--events</c>: consume the global event bus through the Generic Host.</summary>
    Events,

    /// <summary><c>--paginate &lt;sessionId&gt;</c>: enumerate a session's messages page by page.</summary>
    Paginate,

    /// <summary><c>--standalone</c>: the SDK starts a private server and owns it.</summary>
    Standalone,
}
