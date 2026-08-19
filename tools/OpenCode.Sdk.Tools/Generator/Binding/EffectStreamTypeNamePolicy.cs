namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>Names the shared public roles carried by the selected effect-stream contract.</summary>
internal static class EffectStreamTypeNamePolicy
{
    public const string CauseMarkerInterface = "IOpenCodeStreamFailureCause";

    public static string CauseInterface => CSharpNamePolicy.ToUnionInterfaceName("StreamFailureCause");

    public static string CauseVariant(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return $"StreamFailureCause{CSharpNamePolicy.ToPascalCase(tag)}";
    }
}
