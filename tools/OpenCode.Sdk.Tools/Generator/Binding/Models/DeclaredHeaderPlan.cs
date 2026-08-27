namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// One document-declared request header an operation carries. Only the declared name and
/// shape are bound; the value is a caller concern and never enters curation or generated
/// code (ADR-0013).
/// </summary>
internal sealed record DeclaredHeaderPlan
{
    /// <summary>Gets the declared wire header name.</summary>
    public required string WireName { get; init; }

    /// <summary>Gets the emitted method parameter name.</summary>
    public required string Name { get; init; }
}
