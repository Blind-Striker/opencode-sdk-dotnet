using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// The property a marked union dispatches on, with its branches already split by how they tag
/// it: <paramref name="LiteralBranches"/> become variants, and a lone
/// <paramref name="PrefixBranches"/> entry is the candidate arm.
/// </summary>
internal sealed record UnionDiscriminator(
    LiteralMarker Marker,
    IReadOnlyList<ResolvedUnionBranch> LiteralBranches,
    IReadOnlyList<ResolvedUnionBranch> PrefixBranches);
