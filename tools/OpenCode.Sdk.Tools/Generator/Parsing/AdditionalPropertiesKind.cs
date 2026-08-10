namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>How an object schema handles property names outside its declared property bag.</summary>
public enum AdditionalPropertiesKind
{
    /// <summary>Additional properties are permitted without a schema constraint.</summary>
    Open,

    /// <summary>Additional properties are forbidden.</summary>
    Forbidden,

    /// <summary>Additional properties must conform to <see cref="ObjectNode.AdditionalPropertiesSchema"/>.</summary>
    Schema,
}
