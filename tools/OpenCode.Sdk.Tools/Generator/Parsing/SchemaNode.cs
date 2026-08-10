namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Base of every wire-faithful schema-graph node (generator spec §4.1).</summary>
public abstract record SchemaNode
{
    /// <summary>The spec's description text, when present (Binder XML-doc input).</summary>
    public string? Description { get; init; }

    /// <summary>Direct child nodes; drives graph-wide sweeps (dangling-ref validation).</summary>
    public abstract IEnumerable<SchemaNode> Children { get; }
}
