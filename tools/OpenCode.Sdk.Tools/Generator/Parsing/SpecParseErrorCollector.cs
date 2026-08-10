namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Accumulates located parse errors so refusals surface batched, not one at a time.</summary>
internal sealed class SpecParseErrorCollector
{
    private readonly List<string> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public void Add(string location, string problem) => _errors.Add($"{location}: {problem}");

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new SpecParseException([.. _errors]);
        }
    }
}
