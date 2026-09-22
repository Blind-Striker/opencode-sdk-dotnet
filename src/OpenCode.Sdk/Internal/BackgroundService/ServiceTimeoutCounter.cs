namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// Consecutive info-probe timeouts on the same registration identity. Three in a row is the
/// unresponsive-daemon recovery the pinned client's ensure loop runs.
/// </summary>
/// <param name="Identity">The registration the timeouts were counted against.</param>
/// <param name="Count">The consecutive timed-out probes, at least 1.</param>
internal sealed record ServiceTimeoutCounter(ServiceRegistrationIdentity Identity, int Count);
