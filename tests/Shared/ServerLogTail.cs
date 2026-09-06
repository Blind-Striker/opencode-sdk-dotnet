using System.Globalization;

namespace OpenCode.Sdk.TestSupport;

/// <summary>Retains the last 500 lines, each capped at 4,096 characters, for one server stream.</summary>
internal sealed class ServerLogTail
{
    internal const int MaximumLines = 500;
    internal const int MaximumLineCharacters = 4_096;
    private const string Truncated = " [truncated]";
    private readonly Queue<string> _lines = new();
    private long _discardedLines;
    private long _shortenedLines;

    public void Append(string line)
    {
        if (line.Length > MaximumLineCharacters)
        {
            line = line[..(MaximumLineCharacters - Truncated.Length)] + Truncated;
            _shortenedLines++;
        }

        _lines.Enqueue(line);
        if (_lines.Count > MaximumLines)
        {
            _ = _lines.Dequeue();
            _discardedLines++;
        }
    }

    public string? Find(string fragment) =>
        _lines.FirstOrDefault(line => line.Contains(fragment, StringComparison.Ordinal));

    public string Describe() => string.Join(" | ", _lines.Skip(Math.Max(0, _lines.Count - 40)));

    public IReadOnlyList<string> Snapshot() =>
        ["tail: maximum-lines=500 maximum-line-characters=4096 discarded-lines=" +
         _discardedLines.ToString(CultureInfo.InvariantCulture) + " shortened-lines=" +
         _shortenedLines.ToString(CultureInfo.InvariantCulture), .. _lines];
}
