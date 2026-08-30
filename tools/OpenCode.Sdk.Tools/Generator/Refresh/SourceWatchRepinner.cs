using OpenCode.Sdk.Tools.Generator.Refresh.Models;

namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>
/// The apply-time source-watch rule: a reviewed receipt re-pins the committed watch to the
/// hashes it observed. The watch and the receipt must describe the same source set, and an
/// anchor the receipt saw fail is never re-pinned silently — the door's behavior moved, so a
/// human reads the door and rewrites the anchor before the pin advances.
/// </summary>
internal static class SourceWatchRepinner
{
    public static SourceWatch Repin(SourceWatch pinned, IReadOnlyList<ReceiptWatchedSource> observed)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(observed);

        var byPath = observed.ToDictionary(static observation => observation.Path, StringComparer.Ordinal);
        if (byPath.Count != pinned.Sources.Count || pinned.Sources.Any(source => !byPath.ContainsKey(source.Path)))
        {
            throw new SnapshotRefreshException(
                "the source watch names different files than the receipt observed; re-run prepare");
        }

        var unmatched = pinned.Sources.Where(source => !byPath[source.Path].AnchorMatched)
            .Select(static source => source.Path)
            .ToArray();
        if (unmatched.Length > 0)
        {
            throw new SnapshotRefreshException(
                $"the receipt reports lost anchors in {string.Join(", ", unmatched)}; read the hand-written doors and "
                + "rewrite those anchors before applying");
        }

        return pinned with { Sources = [.. pinned.Sources.Select(source => source with { Sha256 = byPath[source.Path].Sha256 })] };
    }
}
