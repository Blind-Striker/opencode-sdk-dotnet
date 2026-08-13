namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed record OperationIdentity(IReadOnlyList<string> Segments, bool HasWildcardPath);
