namespace OpenCode.Sdk.TestSupport;

/// <summary>One provider invocation pushed by the drive backend's llm.request notification.</summary>
internal sealed record DriveInvocation(string Id, string Url);
