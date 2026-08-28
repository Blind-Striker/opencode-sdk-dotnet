namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// One provider invocation pushed by the drive backend's llm.request notification.
/// <paramref name="Model"/> is the model id read out of the provider request body, and is what
/// discriminates one invocation from another when several share a chat route: the url alone
/// cannot tell a scripted turn's request apart from a background model call the server made on
/// its own. It is nullable because the body is provider shaped rather than protocol shaped.
/// </summary>
internal sealed record DriveInvocation(string Id, string Url, string? Model);
