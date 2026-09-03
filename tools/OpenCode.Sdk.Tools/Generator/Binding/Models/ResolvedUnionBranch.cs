using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// One dispatch entry of a marked union. <paramref name="MemberKey"/> is the schema that records
/// membership; when a nested union spans the outer marker, that member differs from the leaf
/// represented by <paramref name="TypeName"/>.
/// </summary>
internal sealed record ResolvedUnionBranch(
    string TypeName,
    IReadOnlyList<LiteralMarker> Markers,
    IReadOnlyList<PrefixMarker> PrefixMarkers,
    bool IsNestedUnion,
    string MemberKey,
    bool IsInhabited);
