namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>The structural wire convention used by an error object.</summary>
public enum ErrorStyle
{
    /// <summary>The object has no recognized error convention.</summary>
    None,

    /// <summary>The object has a required literal <c>_tag</c> property.</summary>
    EffectTag,

    /// <summary>The object has required literal <c>name</c> and required <c>data</c> properties.</summary>
    NameData,
}
