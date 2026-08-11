namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class GraphKeyBuilder
{
    public string Root(string wireNameOrOpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireNameOrOpId);
        return wireNameOrOpId;
    }

    public string Append(string parentPointer, string segment)
    {
        ArgumentNullException.ThrowIfNull(parentPointer);
        ArgumentNullException.ThrowIfNull(segment);

        var escaped = segment
            .Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

        return $"{parentPointer}/{escaped}";
    }
}
