using System.Collections.ObjectModel;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// The mechanical stabilize-duplicate collapse computed for one bind: every reachable
/// <c>&lt;base&gt;_&lt;N&gt;</c> component proven a spelling of <c>&lt;base&gt;</c>, mapped to
/// that base. Refused duplicates are reported to the binding error collector and never appear
/// here, so this set is exactly what the alias map and the generation manifest carry.
/// </summary>
internal sealed record StabilizeDuplicateCollapse
{
    public static StabilizeDuplicateCollapse Empty { get; } = new()
    {
        Aliases = ReadOnlyDictionary<string, string>.Empty,
    };

    /// <summary>Gets each folded duplicate mapped to the base it collapses into.</summary>
    public required IReadOnlyDictionary<string, string> Aliases
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
        }
    }
}
