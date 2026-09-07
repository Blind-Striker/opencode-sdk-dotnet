using System.Globalization;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class EventDiagnosticSummary
{
    internal const string DataKey = "ObservedEvents";
    private const int EdgeCount = 8;
    private const int LabelLength = 96;
    private readonly List<string> _first = [];
    private readonly Queue<string> _tail = new();
    private long _count;
    private readonly Lock _gate = new();

    public void Add(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        lock (_gate)
        {
            // Diagnostics retain eight first and eight last labels, each capped at 96 characters.
            // Successful transcript storage is independent and remains complete.
            label = label.Length <= LabelLength ? label : label[..(LabelLength - 3)] + "...";
            label = label.Replace('\r', ' ').Replace('\n', ' ');
            _count++;
            if (_first.Count < EdgeCount)
            {
                _first.Add(label);
                return;
            }

            if (_tail.Count == EdgeCount)
            {
                _ = _tail.Dequeue();
            }

            _tail.Enqueue(label);
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            var omitted = _count - _first.Count - _tail.Count;
            var middle = omitted > 0
                ? new[] { "... " + omitted.ToString(CultureInfo.InvariantCulture) + " events omitted ..." }
                : [];
            return string.Join(", ", _first.Select(Quote).Concat(middle).Concat(_tail.Select(Quote)));
        }
    }

    private static string Quote(string value) => "'" + value + "'";
}
