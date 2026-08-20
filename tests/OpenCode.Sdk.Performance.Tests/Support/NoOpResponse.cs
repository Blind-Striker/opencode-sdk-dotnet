namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>The envelope the no-op adapter returns: status only, no payload materialization.</summary>
public sealed record NoOpResponse : OpenCodeResponse;
