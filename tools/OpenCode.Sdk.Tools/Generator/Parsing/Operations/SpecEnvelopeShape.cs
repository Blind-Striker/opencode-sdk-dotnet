namespace OpenCode.Sdk.Tools.Generator.Parsing.Operations;

/// <summary>The structural response envelope recognized at parse time.</summary>
public enum SpecEnvelopeShape
{
    /// <summary>The response has no content.</summary>
    None,

    /// <summary>The response content is not one of the recognized JSON envelopes.</summary>
    Bare,

    /// <summary>The JSON object contains exactly a <c>data</c> property.</summary>
    Data,

    /// <summary>The JSON object contains exactly <c>data</c> and <c>location</c> properties.</summary>
    DataLocation,

    /// <summary>The JSON object contains exactly <c>cursor</c> and <c>data</c> properties.</summary>
    CursorData,

    /// <summary>The JSON object contains exactly <c>data</c> and <c>hasMore</c> properties.</summary>
    DataHasMore,
}
