namespace OpenCode.Sdk.Tests.Support;

/// <summary>One request the loopback server read off the socket, as the platform handler wrote it.</summary>
/// <param name="Method">The request method.</param>
/// <param name="Path">The request target.</param>
/// <param name="Body">The declared body, or an empty string when the head declared none.</param>
internal sealed record LoopbackRequest(string Method, string Path, string Body);
