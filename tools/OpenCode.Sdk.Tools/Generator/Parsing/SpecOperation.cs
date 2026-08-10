namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>A single wire operation parsed from a path item.</summary>
public sealed record SpecOperation
{
    /// <summary>The opaque operation identifier authored in the spec.</summary>
    public required string OperationId { get; init; }

    /// <summary>The API surface determined from <see cref="OperationId"/>.</summary>
    public required SpecSurface Surface { get; init; }

    /// <summary>The dot-separated operation-ID segments used by downstream naming.</summary>
    public required IReadOnlyList<string> Segments { get; init; }

    /// <summary>The lowercase HTTP wire verb.</summary>
    public required string Method { get; init; }

    /// <summary>The raw path template, including a trailing wildcard when present.</summary>
    public required string Path { get; init; }

    /// <summary>Whether <see cref="Path"/> ends with the supported terminal wildcard segment.</summary>
    public required bool HasWildcardPath { get; init; }

    /// <summary>Whether the operation is marked as a WebSocket endpoint.</summary>
    public required bool IsWebSocket { get; init; }

    /// <summary>Whether the operation is marked deprecated.</summary>
    public required bool IsDeprecated { get; init; }

    /// <summary>The operation parameters in source document order.</summary>
    public required IReadOnlyList<SpecParameter> Parameters { get; init; }

    /// <summary>The request body, when the operation declares one.</summary>
    public SpecRequestBody? RequestBody { get; init; }

    /// <summary>The optional short operation summary.</summary>
    public string? Summary { get; init; }

    /// <summary>The optional long operation description.</summary>
    public string? Description { get; init; }
}
