using System.Diagnostics.CodeAnalysis;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Folds upstream Effect's stabilize duplicates — component names spelled
/// <c>&lt;base&gt;_&lt;N&gt;</c> that repeat a structurally identical <c>&lt;base&gt;</c> once per
/// use site — into their base before names resolve. This is the same class of mechanical
/// dialect-artifact rule as the <c>*Encoded</c> projection strip
/// (<see cref="ProjectionArtifactNamePolicy"/>): it keys on shape and spelling only, never on an
/// operation id or a family name (ADR-0013).
/// </summary>
/// <remarks>
/// Only a reachable component key whose base exists and is not itself suffixed is a candidate,
/// so the rule never chains: <c>A_1_2</c> is left alone rather than followed through <c>A_1</c> to
/// <c>A</c>. Any other spelling gets no implicit alias and surfaces exactly as an undeclared
/// duplicate does today — a duplicate error tag refuses, a duplicate model breaks the public API
/// baseline — so the worst case is loud. Identity is judged on the graph as it will be bound, so
/// the candidate set shrinks to a fixpoint: a refused duplicate can break a pair that only matched
/// through it, and every survivor is <see cref="SchemaNodeComparer.DeepEquals"/> under the map the
/// survivors themselves form.
/// </remarks>
internal sealed class StabilizeDuplicatePolicy
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    /// <summary>Resolves the implicit alias set, reporting every refused duplicate by both keys.</summary>
    public StabilizeDuplicateCollapse Resolve(SpecDocument document, ReachableSchemaSet reachable,
        BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(errors);

        var candidates = CollectCandidates(document, reachable);
        if (candidates.Count is 0)
        {
            return StabilizeDuplicateCollapse.Empty;
        }

        foreach (var (schema, baseKey) in ReduceToFixpoint(document, candidates))
        {
            errors.Add(
                BindingErrorCategory.Schema,
                schema,
                $"stabilize duplicate '{schema}' is not structurally identical to its base '{baseKey}'");
        }

        return new StabilizeDuplicateCollapse { Aliases = candidates, };
    }

    /// <summary>Every reachable component spelled as a stabilize duplicate of a base the graph declares.</summary>
    private Dictionary<string, string> CollectCandidates(SpecDocument document, ReachableSchemaSet reachable)
    {
        var candidates = new Dictionary<string, string>(_comparer);
        foreach (var key in reachable.GraphKeys)
        {
            if (TryGetStabilizeBase(key, out var baseKey)
                && !TryGetStabilizeBase(baseKey, out _)
                && document.Schemas.ContainsKey(key)
                && document.Schemas.ContainsKey(baseKey))
            {
                candidates[key] = baseKey;
            }
        }

        return candidates;
    }

    /// <summary>
    /// Drops every candidate that is not deeply equal to its base under the surviving map and
    /// repeats until nothing drops, so a refusal cascades into the pairs that leaned on it.
    /// </summary>
    private IReadOnlyList<KeyValuePair<string, string>> ReduceToFixpoint(SpecDocument document,
        Dictionary<string, string> candidates)
    {
        var refused = new List<KeyValuePair<string, string>>();
        bool dropped;
        do
        {
            dropped = false;
            foreach (var candidate in candidates.OrderBy(static pair => pair.Key, _comparer).ToArray())
            {
                if (IsSpellingOfBase(document, candidate, candidates))
                {
                    continue;
                }

                _ = candidates.Remove(candidate.Key);
                refused.Add(candidate);
                dropped = true;
            }
        }
        while (dropped);

        return [.. refused.OrderBy(static pair => pair.Key, _comparer)];
    }

    private static bool IsSpellingOfBase(SpecDocument document, KeyValuePair<string, string> candidate,
        IReadOnlyDictionary<string, string> aliases) =>
        document.Schemas.TryGetValue(candidate.Key, out var duplicate)
        && document.Schemas.TryGetValue(candidate.Value, out var target)
        && SchemaNodeComparer.DeepEquals(duplicate, target, document.Schemas, aliases);

    /// <summary>
    /// Recognizes the component spelling <c>&lt;base&gt;_&lt;N&gt;</c> where <c>N</c> is a
    /// canonical positive integer. Promoted inline keys carry a <c>#</c> and are never component
    /// names, so upstream's suffix convention cannot be read into one by accident.
    /// </summary>
    private static bool TryGetStabilizeBase(string key, [NotNullWhen(true)] out string? baseKey)
    {
        baseKey = null;
        if (key.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        var separator = key.AsSpan().LastIndexOf('_');
        if (separator <= 0 || separator == key.Length - 1)
        {
            return false;
        }

        var suffix = key.AsSpan(separator + 1);
        if (suffix[0] is '0')
        {
            return false;
        }

        foreach (var digit in suffix)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return false;
            }
        }

        baseKey = key[..separator];
        return true;
    }
}
