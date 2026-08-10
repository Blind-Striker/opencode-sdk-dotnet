namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>The API surface identified by an operation's wire identifier.</summary>
public enum SpecSurface
{
    /// <summary>The modern surface identified by a <c>v2</c> operation-ID prefix.</summary>
    Modern,

    /// <summary>The legacy surface identified by an operation ID without that prefix.</summary>
    Legacy,
}
