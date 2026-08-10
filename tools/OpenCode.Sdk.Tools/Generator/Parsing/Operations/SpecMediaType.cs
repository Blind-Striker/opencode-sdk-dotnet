namespace OpenCode.Sdk.Tools.Generator.Parsing.Operations;

/// <summary>A media type with parameters stripped for downstream matching (generator spec §4.1).</summary>
public sealed record SpecMediaType
{
    /// <summary>The media type exactly as authored in the spec, for wire fidelity.</summary>
    public required string Raw { get; init; }

    /// <summary>The type/subtype before the first ';', trimmed and invariant-lowercased.</summary>
    public required string Stripped { get; init; }

    /// <summary>Whether the stripped value is JSON: exactly <c>application/json</c> or a <c>+json</c> suffix.</summary>
    public required bool IsJson { get; init; }

    /// <summary>Whether the stripped value is exactly <c>text/event-stream</c>.</summary>
    public required bool IsEventStream { get; init; }

    /// <summary>Creates the media type from the raw header value, or throws when it is blank or malformed.</summary>
    public static SpecMediaType Create(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

        var stripped = raw.Split(';')[0].Trim().ToLowerInvariant();

        if (!stripped.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Media type '{stripped}' must be type/subtype.", nameof(raw));
        }

        return new SpecMediaType
        {
            Raw = raw,
            Stripped = stripped,
            // The subtype is the string tail, so the +json suffix is matched on the stripped value.
            IsJson = stripped == "application/json" || stripped.EndsWith("+json", StringComparison.Ordinal),
            IsEventStream = stripped == "text/event-stream"
        };
    }
}
