using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed record OperationIdentity(SpecSurface Surface, IReadOnlyList<string> Segments, bool HasWildcardPath);
