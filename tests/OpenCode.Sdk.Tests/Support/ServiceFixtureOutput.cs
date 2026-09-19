using System.Globalization;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// The isolated discovery executable's output as a test reads it: the one stdout line for a found
/// service, and the elapsed-time line it prints on stderr.
/// </summary>
internal static class ServiceFixtureOutput
{
    private const string TookPrefix = "discovery took ";
    private const string TookSuffix = " ms";

    public static string FoundLine(int processId, Uri endpoint) =>
        $"found owns=false pid={processId.ToString(CultureInfo.InvariantCulture)} endpoint={endpoint}";

    /// <summary>Reads the <c>discovery took N ms</c> line; null when the executable never reached it.</summary>
    public static int? DiscoveryMilliseconds(string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);

        var start = standardError.IndexOf(TookPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += TookPrefix.Length;
        var end = standardError.IndexOf(TookSuffix, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        if (end == start)
        {
            return null;
        }

        var milliseconds = 0;
        for (var index = start; index < end; index++)
        {
            var digit = standardError[index] - '0';
            if (digit is < 0 or > 9)
            {
                return null;
            }

            milliseconds = checked((milliseconds * 10) + digit);
        }

        return milliseconds;
    }
}
