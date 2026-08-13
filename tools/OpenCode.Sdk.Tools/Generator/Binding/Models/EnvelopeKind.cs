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
}
