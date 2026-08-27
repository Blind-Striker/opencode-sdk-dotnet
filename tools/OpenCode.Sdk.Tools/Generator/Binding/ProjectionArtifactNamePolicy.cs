namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Strips upstream projection artifacts from component names before .NET name derivation, the
/// same class of mechanical dialect rule as the <c>v2.</c> operation-id prefix strip
/// (ADR-0005). The declared suffix list carries Effect's encode-side <c>*Encoded</c> fallback
/// rename (effect 4.0.0-beta.103; the #44911 projection context). A suffix is stripped from
/// the final dotted segment only when the unsuffixed component does not itself exist, so a
/// deliberate pair such as <c>V2Event</c>/<c>V2EventEncoded</c> keeps both names; if upstream
/// stops emitting the artifact, the rule goes quietly dead.
/// </summary>
internal sealed class ProjectionArtifactNamePolicy
{
    private static readonly string[] ArtifactSuffixes = ["Encoded"];

    private readonly HashSet<string> _componentNames;

    public ProjectionArtifactNamePolicy(IEnumerable<string> componentNames)
    {
        ArgumentNullException.ThrowIfNull(componentNames);
        _componentNames = componentNames.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Returns the component name with any declared projection-artifact suffix removed.</summary>
    public string Normalize(string componentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);

        foreach (var suffix in ArtifactSuffixes)
        {
            if (!componentName.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var stripped = componentName[..^suffix.Length];
            var lastSegmentStart = stripped.LastIndexOf('.', StringComparison.Ordinal) + 1;
            if (stripped.Length == lastSegmentStart || _componentNames.Contains(stripped))
            {
                continue;
            }

            return stripped;
        }

        return componentName;
    }
}
