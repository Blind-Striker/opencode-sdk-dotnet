namespace OpenCode.Sdk.Internal.BackgroundService.Abstractions;

/// <summary>
/// The detached-spawn seam Ensure elections start contenders through: the managed port of the
/// pinned client's <c>spawnServiceContender</c> (<c>service-contender.ts</c>), with Node/libuv's
/// <c>detached: true</c> expressed in platform primitives rather than
/// <see cref="System.Diagnostics.Process"/> (design 6.3), which cannot detach on any current
/// target. Windows spawns through <c>CreateProcessW</c> with
/// <c>DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP</c>, NUL stdin/stdout, and an inheritable
/// stderr pipe under an explicit handle list; Unix spawns through <c>posix_spawnp</c> with
/// <c>POSIX_SPAWN_SETSID</c>, <c>/dev/null</c> stdin/stdout, and a stderr pipe. A missing
/// executable reports synchronously, and the seam refuses rather than falls back when the
/// platform cannot detach.
/// </summary>
internal interface IServiceContenderSpawner
{
    /// <summary>Starts one detached contender and returns its observation handle.</summary>
    /// <param name="startInfo">What to spawn, with which argv and overlaid environment.</param>
    /// <returns>The contender; its pid names the spawned process, or the cmd.exe host for a batch shim.</returns>
    /// <exception cref="OpenCodeServerException">The platform cannot detach, or the spawn itself failed; the inner exception carries the Win32 or errno failure.</exception>
    public ServiceContender Spawn(ContenderStartInfo startInfo);

    /// <summary>
    /// Everything one spawn names. <c>Argv</c> is the complete argv with the file at index zero —
    /// the interpreter when <c>ViaCmdExe</c> routes through cmd.exe, which the caller composed
    /// (batch-shim resolution and quoting stay the composer's job, the launcher's
    /// <c>BatchCommandLine</c> door); the spawner transports it exactly and composes the Windows
    /// command line from it. <c>Environment</c> is the channel-plus-caller overlay the spawner
    /// layers over the launching process's own environment, with the handoff variable applied by
    /// the composer last; a null value removes the variable rather than passing it empty.
    /// </summary>
    /// <param name="Executable">The launcher resolution, kept so a failure names the caller's spelling.</param>
    /// <param name="ViaCmdExe">Whether <c>Argv</c> routes through cmd.exe; fail-closed off Windows.</param>
    /// <param name="Argv">The complete argv, file first.</param>
    /// <param name="Environment">The overlay: channel service-config env, then caller env, then the handoff.</param>
    public sealed record ContenderStartInfo(
        ResolvedExecutable Executable,
        bool ViaCmdExe,
        string[] Argv,
        IReadOnlyDictionary<string, string?> Environment);
}
