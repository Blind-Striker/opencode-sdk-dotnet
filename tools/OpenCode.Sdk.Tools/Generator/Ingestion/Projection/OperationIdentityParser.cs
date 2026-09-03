namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal static class OperationIdentityParser
{
    /// <summary>
    /// The transport prefix every well-formed operation identity carries. Shared rather than
    /// respelled because binding strips it from operation-scoped schema roots too: public
    /// identifiers never carry <c>V2</c> merely because upstream used that transport prefix
    /// (ADR-0005), and one literal keeps the two strips from drifting apart.
    /// </summary>
    internal const string ProtocolPrefix = "v2.";

    /// <summary>Checks whether an operation identity satisfies the protocol-prefix convention.</summary>
    public static bool IsWellFormed(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        return operationId.StartsWith(ProtocolPrefix, StringComparison.Ordinal)
               && !operationId[ProtocolPrefix.Length..].Split('.').Any(string.IsNullOrWhiteSpace);
    }

    public static OperationIdentity? Parse(string operationId, string path, string location, IngestionErrorCollector errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (!operationId.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
        {
            errors.Add(location, $"operationId '{operationId}' does not carry the '{ProtocolPrefix}' protocol prefix");
            return null;
        }

        var segments = operationId[ProtocolPrefix.Length..].Split('.');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(location, $"operationId '{operationId}' contains an empty segment");
            return null;
        }

        var wildcardIndex = path.IndexOf('*', StringComparison.Ordinal);
        var hasWildcard = wildcardIndex >= 0;
        if (!hasWildcard || (wildcardIndex == path.Length - 1 && path.EndsWith("/*", StringComparison.Ordinal)
                                                              && path.LastIndexOf('*', StringComparison.Ordinal) == wildcardIndex))
        {
            return new OperationIdentity(Array.AsReadOnly(segments), hasWildcard);
        }

        errors.Add(location, $"path '{path}' has a wildcard outside the trailing '/*' position");
        return null;
    }
}
