namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>Identifies how the wire success body carries the payload.</summary>
internal enum EnvelopeKind
{
    /// <summary>The body is the payload itself.</summary>
    Bare = 0,

    /// <summary>The body wraps the payload in a required <c>data</c> property.</summary>
    Data = 1,

    /// <summary>The body carries a <c>data</c> array beside a <c>previous</c>/<c>next</c> cursor object.</summary>
    CursorList = 2,

    /// <summary>The success carries no body at all; the response has no payload property.</summary>
    NoContent = 3,

    /// <summary>The body wraps a <c>data</c> object beside a required <c>location</c> echo.</summary>
    DataLocation = 4,

    /// <summary>The body carries a <c>data</c> array beside a required <c>location</c> echo.</summary>
    DataLocationList = 5,
}
