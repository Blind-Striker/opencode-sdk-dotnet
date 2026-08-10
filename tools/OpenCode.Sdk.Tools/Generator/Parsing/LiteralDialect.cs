namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>The supported schema spellings for a single literal.</summary>
public enum LiteralDialect
{
    /// <summary>An enum containing exactly one value.</summary>
    SingleValueEnum,

    /// <summary>A JSON Schema const value.</summary>
    Const,
}
