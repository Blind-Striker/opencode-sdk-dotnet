using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Chooses the directory owned run roots are created in. The pinned server walks from every
/// location to the drive root (<see cref="RunRootAncestry"/>), so the choice is about the chain
/// above a workspace, not only the directory itself. The OS temp root has a clean chain except on
/// Windows, where it sits inside the user profile and a workspace beneath it walks up through the
/// developer's own home - which redirecting the home variables does not change. The repository is
/// no alternative: a workspace inside it would belong to the checkout's own project. There the run
/// roots live under the machine-wide application data root, the standard user-writable directory
/// that is outside both.
/// </summary>
internal sealed class TestRunRootLocation
{
    /// <summary>Names a directory to create run roots in instead of the platform default.</summary>
    public const string OverrideVariable = "OPENCODE_SDK_TESTS_RUN_ROOT";

    private readonly IFileSystem _fileSystem;
    private readonly string? _configured;

    public TestRunRootLocation(IFileSystem fileSystem, string? configured)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
        _configured = configured;
    }

    /// <summary>The policy over this process's own environment.</summary>
    public static TestRunRootLocation ForCurrentProcess(IFileSystem fileSystem) =>
        new(fileSystem, Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>Resolves the directory, refusing one whose ancestors would steer every owned server.</summary>
    public string Resolve()
    {
        var directory = _fileSystem.Path.GetFullPath(Choose());
        if (new RunRootAncestry(_fileSystem).FindDeveloperState(directory) is { } state)
        {
            throw new InvalidOperationException(
                $"Test run roots under '{directory}' would inherit '{state}': the pinned server loads project "
                + "configuration from every directory above a workspace, so every owned server would read it. "
                + $"Set {OverrideVariable} to a directory with nothing of the kind above it.");
        }

        return directory;
    }

    private string Choose()
    {
        // The pattern narrows the configured value to non-null on every target framework.
        if (_configured is { Length: > 0 } configured)
        {
            return configured;
        }

        var parent = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : _fileSystem.Path.GetTempPath();
        return _fileSystem.Path.Combine(parent, "opencode-sdk-tests");
    }
}
