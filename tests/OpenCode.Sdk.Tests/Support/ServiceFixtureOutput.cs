using System.Globalization;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>The one stdout line the isolated discovery executable prints for a found service, as a test expects it.</summary>
internal static class ServiceFixtureOutput
{
    public static string FoundLine(int processId, Uri endpoint) =>
        $"found owns=false pid={processId.ToString(CultureInfo.InvariantCulture)} endpoint={endpoint}";
}
