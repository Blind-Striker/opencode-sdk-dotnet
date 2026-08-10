namespace OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

/// <summary>The scalar kind represented by a primitive schema node.</summary>
public enum PrimitiveKind
{
    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number.</summary>
    Number,

    /// <summary>A JSON integer.</summary>
    Integer,

    /// <summary>A JSON boolean.</summary>
    Boolean,
}
