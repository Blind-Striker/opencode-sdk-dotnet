using OpenCode.Sdk.Tools.Generator.Parsing.Operations;
using OpenCode.Sdk.Tools.Generator.Parsing.Schemas;

namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>The wire-faithful SpecIR root: operations plus the flat schema graph.</summary>
public sealed record SpecDocument
{
    /// <summary>The document's <c>openapi</c> version string (always 3.1.x).</summary>
    public required string OpenApiVersion { get; init; }

    /// <summary>All operations, in document order.</summary>
    public required IReadOnlyList<SpecOperation> Operations { get; init; }

    /// <summary>Named schemas plus promoted inline types, keys ordinal-sorted.</summary>
    public required IReadOnlyDictionary<string, SchemaNode> Schemas { get; init; }
}
