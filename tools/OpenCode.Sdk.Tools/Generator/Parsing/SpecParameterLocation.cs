namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>The wire location from which an operation parameter is read.</summary>
public enum SpecParameterLocation
{
    /// <summary>A token embedded in the operation path template.</summary>
    Path,

    /// <summary>A query-string value.</summary>
    Query,

    /// <summary>An HTTP header value.</summary>
    Header,
}
