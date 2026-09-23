using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using static OpenCode.Sdk.Internal.BackgroundService.ProcessControl.BackgroundServiceInterop.Libc;

namespace OpenCode.Sdk.Internal.BackgroundService.Contender;

/// <summary>The Unix arm: <c>posix_spawnp</c> with <c>POSIX_SPAWN_SETSID</c>, <c>/dev/null</c> on 0 and 1, the stderr pipe on 2, and default signal state.</summary>
internal sealed partial class ServiceContenderSpawner
{
    /// <summary><c>O_RDONLY</c> / <c>O_WRONLY</c> for the <c>/dev/null</c> redirections.</summary>
    private const int ReadOnly = 0;

    /// <summary><c>O_WRONLY</c> for the <c>/dev/null</c> stdout redirection.</summary>
    private const int WriteOnly = 1;

    /// <summary><c>POSIX_SPAWN_SETSIGDEF</c>, the same value on glibc, musl, and Darwin: the default-disposition set applies.</summary>
    private const short ResetSignalDispositions = 0x04;

    /// <summary><c>POSIX_SPAWN_SETSIGMASK</c>, the same value on glibc, musl, and Darwin: the mask set applies.</summary>
    private const short ResetSignalMask = 0x08;

    /// <summary>The opaque <c>sigset_t</c>: 128 bytes on glibc, 4 on Darwin, so one buffer fits both.</summary>
    private const int SignalSetCapacity = 128;

    /// <summary><c>POSIX_SPAWN_SETSID</c> on Linux; macOS uses a different value, hence the branch.</summary>
    private const short LinuxNewSession = 0x80;

    /// <summary><c>POSIX_SPAWN_SETSID</c> on macOS.</summary>
    private const short MacNewSession = 0x400;

    /// <summary>
    /// The spawn-attribute and file-action objects are opaque and sized by the C library, so both
    /// ride in deliberately oversized pinned buffers the init calls shape; the destroy calls in the
    /// matching finally blocks give them back.
    /// </summary>
    private const int FileActionsCapacity = 256;

    private const int SpawnAttributesCapacity = 1024;

    private static ServiceContender SpawnUnix(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        Dictionary<string, string> environment,
        string? redaction)
    {
        var arguments = new List<string>(startInfo.Arguments.Count + 1) { startInfo.Executable.Path };
        arguments.AddRange(startInfo.Arguments);
        var variables = new List<string>(environment.Count);
        variables.AddRange(environment.Select(static entry => entry.Key + "=" + entry.Value));

        var argv = AllocArgumentVector(arguments);
        try
        {
            var envp = AllocArgumentVector(variables);
            try
            {
                return SpawnUnixChild(startInfo, argv, envp, redaction);
            }
            finally
            {
                FreeArgumentVector(envp, variables.Count);
            }
        }
        finally
        {
            FreeArgumentVector(argv, arguments.Count);
        }
    }

    /// <summary>
    /// The Unix spawn. The stderr pipe is the BCL's anonymous pipe, both ends close-on-exec as the
    /// runtime creates them, so no other child of this host inherits either end; only the write end
    /// crosses, as fd 2 through the file action's <c>dup2</c>, which clears close-on-exec on the
    /// target alone.
    /// </summary>
    private static ServiceContender SpawnUnixChild(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        IntPtr argv,
        IntPtr envp,
        string? redaction)
    {
        AnonymousPipeServerStream? pipe = new(PipeDirection.In, HandleInheritability.None);
        var actions = new byte[FileActionsCapacity];
        var attributes = new byte[SpawnAttributesCapacity];
        var actionsHandle = GCHandle.Alloc(actions, GCHandleType.Pinned);
        var attributesHandle = GCHandle.Alloc(attributes, GCHandleType.Pinned);
        try
        {
            var actionsPtr = actionsHandle.AddrOfPinnedObject();
            var attributesPtr = attributesHandle.AddrOfPinnedObject();
            CheckNativeResult(startInfo, InitFileActions(actionsPtr));
            try
            {
                CheckNativeResult(startInfo, InitAttributes(attributesPtr));
                try
                {
                    ConfigureAttributes(startInfo, attributesPtr);
                    var writeEnd = (int)pipe.ClientSafePipeHandle.DangerousGetHandle();
                    CheckNativeResult(startInfo, AddOpenFileAction(actionsPtr, 0, "/dev/null", ReadOnly, 0));
                    CheckNativeResult(startInfo, AddOpenFileAction(actionsPtr, 1, "/dev/null", WriteOnly, 0));
                    CheckNativeResult(startInfo, AddDuplicateAction(actionsPtr, writeEnd, 2));

                    // posix_spawn reports the errno as its return value rather than through the
                    // thread's errno, so the value below — not GetLastWin32Error — is the failure.
                    var spawned = SpawnProcess(out var pid, startInfo.Executable.Path, actionsPtr, attributesPtr, argv, envp);

                    // The parent never holds the write end: keeping it would keep EOF away after
                    // every writer is gone.
                    pipe.DisposeLocalCopyOfClientHandle();
                    if (spawned != 0)
                    {
                        throw SpawnFailure(startInfo, new Win32Exception(spawned));
                    }

                    var contender = new ServiceContender(pid, pipe, null, redaction);
                    pipe = null;
                    return contender;
                }
                finally
                {
                    _ = DestroyAttributes(attributesPtr);
                }
            }
            finally
            {
                _ = DestroyFileActions(actionsPtr);
            }
        }
        finally
        {
            actionsHandle.Free();
            attributesHandle.Free();
            pipe?.Dispose();
        }
    }

    /// <summary>
    /// The session flag, plus the signal state libuv gives a Node child: every signal back at its
    /// default disposition and an empty mask. <c>posix_spawn</c> alone resets only handled signals
    /// and keeps ignored ones (the .NET runtime ignores <c>SIGPIPE</c>) and the calling thread's
    /// mask. The attribute calls copy the set, so its buffer lives only for these calls; it is
    /// oversized and zeroed, the discipline the attribute and file-action buffers follow.
    /// </summary>
    private static void ConfigureAttributes(IServiceContenderSpawner.ContenderStartInfo startInfo, IntPtr attributes)
    {
        CheckNativeResult(
            startInfo,
            SetAttributeFlags(attributes, (short)(SpawnFlags() | ResetSignalDispositions | ResetSignalMask)));
        var set = Marshal.AllocHGlobal(SignalSetCapacity);
        try
        {
            Marshal.Copy(new byte[SignalSetCapacity], 0, set, SignalSetCapacity);
            CheckSignalSetResult(startInfo, FillSignalSet(set));
            CheckNativeResult(startInfo, SetDefaultSignals(attributes, set));
            CheckSignalSetResult(startInfo, EmptySignalSet(set));
            CheckNativeResult(startInfo, SetSignalMask(attributes, set));
        }
        finally
        {
            Marshal.FreeHGlobal(set);
        }
    }

    /// <summary>The set functions report through errno, unlike the <c>posix_spawn</c> family, which returns it.</summary>
    private static void CheckSignalSetResult(IServiceContenderSpawner.ContenderStartInfo startInfo, int result)
    {
        if (result != 0)
        {
            throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    private static void CheckNativeResult(IServiceContenderSpawner.ContenderStartInfo startInfo, int result)
    {
        if (result != 0)
        {
            throw SpawnFailure(startInfo, new Win32Exception(result));
        }
    }

    /// <summary>
    /// The session flag differs by kernel, and anything else fails closed: without a session the
    /// contender would stay in the launcher's group, which the detach contract forbids.
    /// </summary>
    private static short SpawnFlags()
    {
#if NET
        if (OperatingSystem.IsLinux())
        {
            return LinuxNewSession;
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacNewSession;
        }
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return LinuxNewSession;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return MacNewSession;
        }
#endif
        throw new OpenCodeServerException(
            "The background service contender needs a detached session the platform does not provide: no contender was started.");
    }

    /// <summary>Marshals one UTF-8 argument vector: a null-terminated array of null-terminated byte strings.</summary>
    private static IntPtr AllocArgumentVector(List<string> values)
    {
        var vector = Marshal.AllocHGlobal((values.Count + 1) * IntPtr.Size);
        var completed = false;
        try
        {
            // Zeroed first, so the failure cleanup below frees only what was actually stored.
            for (var index = 0; index <= values.Count; index++)
            {
                Marshal.WriteIntPtr(vector, index * IntPtr.Size, IntPtr.Zero);
            }

            for (var index = 0; index < values.Count; index++)
            {
                var bytes = Encoding.UTF8.GetBytes(values[index]);
                var slot = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, slot, bytes.Length);
                Marshal.WriteByte(slot, bytes.Length, 0);
                Marshal.WriteIntPtr(vector, index * IntPtr.Size, slot);
            }

            Marshal.WriteIntPtr(vector, values.Count * IntPtr.Size, IntPtr.Zero);
            completed = true;
            return vector;
        }
        finally
        {
            if (!completed)
            {
                FreeArgumentVector(vector, values.Count);
            }
        }
    }

    private static void FreeArgumentVector(IntPtr vector, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var slot = Marshal.ReadIntPtr(vector, index * IntPtr.Size);
            if (slot != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(slot);
            }
        }

        Marshal.FreeHGlobal(vector);
    }
}
