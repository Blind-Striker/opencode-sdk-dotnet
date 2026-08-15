namespace OpenCode.Sdk;

/// <summary>
/// Selects the directory and workspace a request addresses; unset members leave the
/// server's own resolution in place. Rides the wire as <c>location[directory]</c> and
/// <c>location[workspace]</c> query keys.
/// </summary>
public sealed record LocationSelector
{
    /// <summary>Gets the directory the request addresses.</summary>
    public string? Directory { get; init; }

    /// <summary>Gets the workspace the request addresses.</summary>
    public string? Workspace { get; init; }
}
