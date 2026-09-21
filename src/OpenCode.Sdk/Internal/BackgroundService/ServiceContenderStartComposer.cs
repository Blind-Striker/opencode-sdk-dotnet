using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// Composes one <see cref="IServiceContenderSpawner.ContenderStartInfo"/> from a resolved
/// executable, the remaining argv, and the overlay environment. Batch shims route through cmd.exe
/// the way the launcher does; the refuse of cmd metacharacters is
/// <see cref="BatchCommandLine.Compose"/>.
/// </summary>
internal sealed class ServiceContenderStartComposer
{
    /// <summary>Builds the spawn request.</summary>
    /// <param name="executable">The launcher resolution of command[0].</param>
    /// <param name="arguments">The remaining argv after the executable.</param>
    /// <param name="environment">The config-plus-caller-plus-handoff overlay.</param>
    /// <returns>The start info the spawner transports.</returns>
    public static IServiceContenderSpawner.ContenderStartInfo Compose(
        ResolvedExecutable executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        if (executable.IsBatchScript)
        {
            _ = BatchCommandLine.Compose(executable.Path, arguments, launcherArguments: []);
            var argv = new string[arguments.Count + 5];
            argv[0] = BatchCommandLine.InterpreterPath;
            argv[1] = "/d";
            argv[2] = "/s";
            argv[3] = "/c";
            argv[4] = executable.Path;
            for (var index = 0; index < arguments.Count; index++)
            {
                argv[index + 5] = arguments[index];
            }

            return new IServiceContenderSpawner.ContenderStartInfo(executable, ViaCmdExe: true, argv, environment);
        }

        var direct = new string[arguments.Count + 1];
        direct[0] = executable.Path;
        for (var index = 0; index < arguments.Count; index++)
        {
            direct[index + 1] = arguments[index];
        }

        return new IServiceContenderSpawner.ContenderStartInfo(executable, ViaCmdExe: false, direct, environment);
    }
}
