namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>The observational verify verdict: an empty problem list is a reproduced identity.</summary>
internal sealed record VerifyOutcome
{
    public required string UpstreamCommit { get; init; }

    public required IReadOnlyList<string> Problems
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    }

    public bool IsReproduced => Problems.Count is 0;
}
