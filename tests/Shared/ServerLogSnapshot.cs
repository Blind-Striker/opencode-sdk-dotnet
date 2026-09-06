namespace OpenCode.Sdk.TestSupport;

internal sealed record ServerLogSnapshot
{
    public required IReadOnlyList<string> StandardOutput { get; init; }

    public required IReadOnlyList<string> StandardError { get; init; }
}
