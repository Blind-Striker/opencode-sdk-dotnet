using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// An enum value's member name: its reason-bearing row when one exists, else its mechanical
/// Pascal casing. The binder names members with it and the curation validator checks collisions
/// with it, so the two can never disagree about a name.
/// </summary>
internal static class EnumMemberNamePolicy
{
    public static string Name(string schema, string value, IReadOnlyList<EnumMemberNameCuration> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
                   .FirstOrDefault(row => string.Equals(row.Schema, schema, StringComparison.Ordinal)
                       && string.Equals(row.Value, value, StringComparison.Ordinal))?.DotNetName
               ?? CSharpNamePolicy.ToPascalCase(value);
    }
}
