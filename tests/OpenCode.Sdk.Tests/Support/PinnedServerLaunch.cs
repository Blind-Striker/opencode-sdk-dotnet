using System.IO.Abstractions;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// The one launch arrangement that actually boots the pinned source server, so every launcher
/// test states only its own variation instead of repeating the arrangement.
/// </summary>
internal sealed class PinnedServerLaunch
{
    private readonly IFileSystem _fileSystem;
    private readonly PinnedServerCommand _command;

    public PinnedServerLaunch(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        _command = new PinnedServerCommand(fileSystem);
    }

    /// <summary>Gets bun running the submodule's CLI entry: the executable and its leading arguments.</summary>
    public IReadOnlyList<string> Command => _command.Resolve();

    /// <summary>
    /// Gets the child's working directory. Bun's own workspace/tsconfig discovery for the pinned
    /// monorepo's JSX packages (the TUI's solid-js tree) walks from the process's working
    /// directory, not from the absolute entry-file path. A working directory outside the checkout
    /// (the original design of an isolated per-run scratch "cwd") leaves that discovery unable to
    /// find the workspace root, and the source-run server fails before readiness with "Cannot find
    /// module 'react/jsx-dev-runtime'" — confirmed by direct repro. Anchoring at the CLI package
    /// (this repo's own historical smoke-test convention) is what upstream's own "dev" script does;
    /// state/data/cache/config stay isolated through the environment regardless of this directory.
    /// </summary>
    public string WorkingDirectory =>
        _fileSystem.Path.Combine(_command.RepositoryRoot, "external", "opencode", "packages", "cli");

    /// <summary>Builds the launch options, optionally against a command that stands in for the pinned one.</summary>
    /// <param name="runRoot">The per-run root every global state directory is redirected into.</param>
    /// <param name="command">A stand-in command; null launches the pinned one directly.</param>
    /// <returns>Fresh options, safe for the caller to shape further.</returns>
    public OpenCodeServerOptions Options(TestRunRoot runRoot, IReadOnlyList<string>? command = null)
    {
        ArgumentNullException.ThrowIfNull(runRoot);

        return new OpenCodeServerOptions
        {
            Command = command ?? Command,
            WorkingDirectory = WorkingDirectory,
            Environment = ServerIsolation.Environment(_fileSystem, runRoot.Path),
            ReadinessTimeout = TimeSpan.FromMinutes(3),
        };
    }
}
