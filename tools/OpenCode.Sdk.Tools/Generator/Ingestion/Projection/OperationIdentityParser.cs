namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class OperationIdentityParser
{
    private readonly string _protocolPrefix = "v2.";

    public OperationIdentity? Parse(string operationId, string path, string location, IngestionErrorCollector errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (!operationId.StartsWith(_protocolPrefix, StringComparison.Ordinal))
        {
            errors.Add(location, $"operationId '{operationId}' does not carry the '{_protocolPrefix}' protocol prefix");
            return null;
        }

        var segments = operationId[_protocolPrefix.Length..].Split('.');
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
