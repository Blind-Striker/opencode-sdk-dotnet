using OpenCode.Sdk.Tools.Generator.Binding;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>Renders a generated identifier as the prose the emitted XML documentation reads.</summary>
internal static class GeneratedDisplayName
{
    public static string Of(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return string.Join(' ', CSharpNamePolicy.SplitWords(name).Select(static word => word.ToLowerInvariant()));
    }
}
