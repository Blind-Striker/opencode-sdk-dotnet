using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Resolves the exact-pin simulation host profile that retains durable events for session-log
/// coverage. The ordinary CLI server profile deliberately remains unchanged.
/// </summary>
internal sealed class PersistentSimulationServerCommand
{
    private const string HostFixture = "Server.persistent-simulation-host.js";
    private readonly FixtureLoader _fixtures = new();
    private readonly PinnedServerCommand _pinned;

    public PersistentSimulationServerCommand(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _pinned = new PinnedServerCommand(fileSystem);
    }

    public string RepositoryRoot => _pinned.RepositoryRoot;

    public IReadOnlyList<string> Resolve()
    {
        // Retain the standard resolver's fail-fast checks for the exact-pin source and install.
        _ = _pinned.Resolve();
        return ["bun", "-e", _fixtures.LoadText(HostFixture)];
    }
}
