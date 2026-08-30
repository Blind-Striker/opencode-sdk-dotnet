using OpenCode.Sdk.Tools.Generator.Refresh.Models;

namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>
/// Compares the committed source-watch pins against one observation of the same sources. Every
/// disagreement is a review trigger naming the door behavior at stake — a changed blob, a lost
/// anchor, or a source the observation never saw.
/// </summary>
internal static class SourceWatchVerifier
{
    public static IReadOnlyList<string> Compare(IReadOnlyList<WatchedSource> pinned, IReadOnlyList<ReceiptWatchedSource> observed)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(observed);

        var byPath = observed.ToDictionary(static observation => observation.Path, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var source in pinned)
        {
            if (!byPath.TryGetValue(source.Path, out var observation))
            {
                problems.Add($"watched source '{source.Path}' was not observed");
                continue;
            }

            if (!string.Equals(observation.Sha256, source.Sha256, StringComparison.Ordinal))
            {
                problems.Add(
                    $"watched source '{source.Path}' changed: pinned {source.Sha256}, found {observation.Sha256}; "
                    + $"review the hand-written door against {source.Behavior}");
            }

            if (!observation.AnchorMatched)
            {
                problems.Add(
                    $"watched source '{source.Path}' no longer carries its anchor ({source.Anchor.Type} '{source.Anchor.Text}'): "
                    + source.Behavior);
            }
        }

        return Array.AsReadOnly([.. problems]);
    }
}
