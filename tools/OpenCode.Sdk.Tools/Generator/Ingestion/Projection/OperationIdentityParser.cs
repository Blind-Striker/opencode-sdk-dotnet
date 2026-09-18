namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal static class OperationIdentityParser
{
    /// <summary>
    /// Every upstream group id is <c>server.&lt;name&gt;</c>, so an endpoint that omits its own identifier
    /// is emitted as <c>server.&lt;name&gt;.&lt;endpoint&gt;</c>. The server group's own operations are the only
    /// identities that legitimately start with this segment, and they have exactly two segments.
    /// </summary>
    private const string GroupQualifier = "server";

    /// <summary>Checks for a group/action identity without the Effect group qualification defect.</summary>
    public static bool IsWellFormed(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var segments = operationId.Split('.');
        return segments.Length >= 2
               && !segments.Any(string.IsNullOrWhiteSpace)
               && !(segments.Length > 2 && string.Equals(segments[0], GroupQualifier, StringComparison.Ordinal));
    }

    public static OperationIdentity? Parse(string operationId, string path, string location, IngestionErrorCollector errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        var segments = operationId.Split('.');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(location, $"operationId '{operationId}' contains an empty segment");
            return null;
        }

        if (!IsWellFormed(operationId))
        {
            errors.Add(location, $"operationId '{operationId}' does not satisfy the group/action convention; upstream identity defects require an explicit curation repair");
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
