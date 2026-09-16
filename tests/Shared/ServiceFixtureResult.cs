namespace OpenCode.Sdk.TestSupport;

/// <summary>One completed fixture run: the exit code and both output streams, never a credential.</summary>
internal sealed record ServiceFixtureResult(int ExitCode, string StandardOutput, string StandardError);
