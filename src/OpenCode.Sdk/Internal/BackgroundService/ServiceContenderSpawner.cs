using System.Collections;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The shipped <see cref="IServiceContenderSpawner"/> on the platform's own spawn primitives
/// (ADR-0027), with the stderr pipe taken from the BCL. The modern targets take
/// <c>LibraryImport</c>, the compile-time stub .NET recommends; the <c>netstandard2.0</c> asset,
/// where that generator is unavailable, takes the equivalent <c>DllImport</c> — the
/// <c>ServiceProcessControl</c> precedent, Mono included.
/// </summary>
internal sealed partial class ServiceContenderSpawner : IServiceContenderSpawner
{
    /// <summary>The PTY-handoff variable whose value is secret-bearing: removed when empty, redacted from diagnostics when present.</summary>
    private const string HandoffVariable = "OPENCODE_PTY_HANDOFF";

    /// <summary><c>DETACHED_PROCESS</c>: no console, and the parent's console control events never reach the contender.</summary>
    private const uint DetachedProcess = 0x08000000;

    /// <summary><c>CREATE_NEW_PROCESS_GROUP</c>: the contender roots its own group, Ctrl+C included.</summary>
    private const uint NewProcessGroup = 0x00000200;

    /// <summary><c>CREATE_UNICODE_ENVIRONMENT</c>: the environment block below is UTF-16.</summary>
    private const uint UnicodeEnvironment = 0x00000400;

    /// <summary><c>EXTENDED_STARTUPINFO_PRESENT</c>: the startup info carries the attribute list.</summary>
    private const uint ExtendedStartupInfo = 0x00080000;

    /// <summary><c>STARTF_USESTDHANDLES</c>: the three standard handles come from the startup info.</summary>
    private const uint UseStandardHandles = 0x00000100;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

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

    /// <summary><c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>: only these handles cross into the child.</summary>
    private static readonly IntPtr HandleListAttribute = new(0x20002);

    /// <summary><c>INVALID_HANDLE_VALUE</c>, what <c>CreateFileW</c> returns on failure.</summary>
    private static readonly IntPtr InvalidHandle = new(-1);

    public ServiceContender Spawn(IServiceContenderSpawner.ContenderStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.Executable is null || startInfo.Arguments is null || startInfo.Environment is null)
        {
            throw new ArgumentNullException(nameof(startInfo));
        }

        if (string.IsNullOrWhiteSpace(startInfo.Executable.Path))
        {
            throw new ArgumentException(
                "ContenderStartInfo.Executable must name the file to spawn.", nameof(startInfo));
        }

        if (startInfo.Environment.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("ContenderStartInfo.Environment keys cannot be blank.", nameof(startInfo));
        }

        if (startInfo.Environment.Keys.Any(ContainsNul) ||
            startInfo.Environment.Values.Any(static value => value is not null && ContainsNul(value)))
        {
            throw new ArgumentException("ContenderStartInfo.Environment keys and values cannot contain NUL.", nameof(startInfo));
        }

        if (startInfo.Executable.IsBatchScript && !IsWindows)
        {
            throw new OpenCodeServerException(
                "The background service contender routes through cmd.exe, which exists only on Windows: no contender was started.");
        }

        var environment = MergeEnvironment(startInfo.Environment, out var redaction);
        return IsWindows
            ? SpawnWindows(startInfo, environment, redaction)
            : SpawnUnix(startInfo, environment, redaction);
    }

    /// <summary>
    /// The NUL scan the analyzer wall leaves: <c>Contains(char)</c> and <c>IndexOf(char)</c>
    /// trade CA1307/MA0001 against CA2249, and the <c>StringComparison</c> overload is absent
    /// downlevel, so the check is a plain loop.
    /// </summary>
    private static bool ContainsNul(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\0')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Layers the overlay over the launching process's own environment, the way the pinned client
    /// spreads <c>process.env</c> under its service env. A null overlay value removes the variable;
    /// an empty handoff is removed too rather than passed as an empty ticket.
    /// </summary>
    private static Dictionary<string, string?> MergeEnvironment(
        IReadOnlyDictionary<string, string?> overlay, out string? redaction)
    {
        var merged = new Dictionary<string, string?>(
            IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                merged[key] = value;
            }
        }

        foreach (var entry in overlay)
        {
            if (entry.Value is null)
            {
                merged.Remove(entry.Key);
            }
            else
            {
                merged[entry.Key] = entry.Value;
            }
        }

        redaction = null;
        if (merged.TryGetValue(HandoffVariable, out var ticket) && !string.IsNullOrEmpty(ticket))
        {
            redaction = ticket;
        }
        else
        {
            merged.Remove(HandoffVariable);
        }

        return merged;
    }

    private static ServiceContender SpawnWindows(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        Dictionary<string, string?> environment,
        string? redaction)
    {
        // The native call may scribble over its buffer, so the line crosses as pinned UTF-16 rather
        // than a string the marshaller would have to copy back.
        var commandLine = Marshal.StringToHGlobalUni(WindowsCommandLine(startInfo));
        try
        {
            return SpawnWindowsChild(startInfo, commandLine, WindowsEnvironmentBlock.Build(environment), redaction);
        }
        finally
        {
            Marshal.FreeHGlobal(commandLine);
        }
    }

    /// <summary>
    /// The launcher's two Windows spellings (<c>OpenCodeServer.ConfigureCommandLine</c>): a batch
    /// shim runs through the system cmd.exe on the <see cref="BatchCommandLine"/> line, whose
    /// outer quote pair is what <c>/s</c> strips, and whose metacharacter refusal throws before
    /// anything spawns; anything else is its argv through the MSVCRT quoting, so a path with
    /// spaces survives the round trip the way <c>ArgumentList</c> survives it.
    /// </summary>
    private static string WindowsCommandLine(IServiceContenderSpawner.ContenderStartInfo startInfo)
    {
        var executable = startInfo.Executable;
        if (executable.IsBatchScript)
        {
            return "\"" + BatchCommandLine.InterpreterPath + "\" " +
                BatchCommandLine.Compose(executable.Path, startInfo.Arguments, launcherArguments: []);
        }

        return ProcessArgumentComposer.Compose([executable.Path, .. startInfo.Arguments]);
    }

    /// <summary>
    /// The Windows spawn. The stderr pipe is the BCL's anonymous pipe, created the way .NET's own
    /// <c>Process</c> creates its pipes: only the child's write end is inheritable, and it crosses
    /// alone beside NUL in the explicit handle list, so no later child of this host inherits the
    /// read end. NUL stays a raw handle until the call returns, because the attribute list needs
    /// raw values; the finally below closes whatever is still owned here.
    /// </summary>
    private static ServiceContender SpawnWindowsChild(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        IntPtr commandLine,
        string environmentBlock,
        string? redaction)
    {
        var security = new SecurityAttributes
        {
            Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
            SecurityDescriptor = IntPtr.Zero,
            InheritHandle = 1,
        };

        var nul = IntPtr.Zero;
        AnonymousPipeServerStream? pipe = null;
        SafeProcessHandle? process = null;
        try
        {
            pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            nul = OpenNullDevice(
                "NUL", GenericRead | GenericWrite, ShareReadWrite, ref security, OpenExisting, 0, IntPtr.Zero);
            if (nul == IntPtr.Zero || nul == InvalidHandle)
            {
                var error = Marshal.GetLastWin32Error();
                nul = IntPtr.Zero;
                throw SpawnFailure(startInfo, new Win32Exception(error));
            }

            var inherited = new[] { nul, pipe.ClientSafePipeHandle.DangerousGetHandle() };
            var attributes = CreateHandleAttributeList(startInfo, inherited, out var pin);
            try
            {
                var info = LaunchWindowsChild(startInfo, commandLine, environmentBlock, inherited, attributes);

                // The thread handle is never needed again; the process handle becomes the
                // contender's exit poll.
                using (new SafeProcessHandle(info.ProcessThread, ownsHandle: true))
                {
                    process = new SafeProcessHandle(info.Process, ownsHandle: true);
                }

                // The child holds its own copy now; keeping ours would keep EOF away after every
                // writer is gone.
                pipe.DisposeLocalCopyOfClientHandle();
                var contender = new ServiceContender((int)info.ProcessId, pipe, process, redaction);
                pipe = null;
                process = null;
                return contender;
            }
            finally
            {
                pin.Free();
                DeleteAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
        }
        finally
        {
            if (nul != IntPtr.Zero)
            {
                _ = CloseHandle(nul);
            }

            pipe?.Dispose();
            process?.Dispose();
        }
    }

    private static ProcessInformation LaunchWindowsChild(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        IntPtr commandLine,
        string environmentBlock,
        IntPtr[] inherited,
        IntPtr attributes)
    {
        var startup = new StartupInfoEx
        {
            Startup = new StartupInfo
            {
                StructureSize = (uint)Marshal.SizeOf<StartupInfoEx>(),
                Flags = UseStandardHandles,
                StandardInput = inherited[0],
                StandardOutput = inherited[0],
                StandardError = inherited[1],
            },
            AttributeList = attributes,
        };
        if (!CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: true,
                DetachedProcess | NewProcessGroup | UnicodeEnvironment | ExtendedStartupInfo,
                environmentBlock,
                null,
                ref startup,
                out var info))
        {
            throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return info;
    }

    /// <summary>
    /// Sizes, allocates, and fills the explicit inherited-handle list: NUL for stdin/stdout and
    /// the stderr pipe's write end, and nothing else — no unrelated inheritable handle leaks into
    /// the contender the way a blanket <c>bInheritHandles</c> spawn would leak them.
    /// </summary>
    private static IntPtr CreateHandleAttributeList(
        IServiceContenderSpawner.ContenderStartInfo startInfo, IntPtr[] handles, out GCHandle pin)
    {
        var size = IntPtr.Zero;
        _ = InitializeAttributeList(IntPtr.Zero, 1, 0, ref size);
        if (size == IntPtr.Zero)
        {
            throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var list = Marshal.AllocHGlobal(size);
        GCHandle hold = default;
        var completed = false;
        try
        {
            if (!InitializeAttributeList(list, 1, 0, ref size))
            {
                throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
            }

            hold = GCHandle.Alloc(handles, GCHandleType.Pinned);
            if (!UpdateAttribute(
                    list,
                    0,
                    HandleListAttribute,
                    hold.AddrOfPinnedObject(),
                    new IntPtr(handles.Length * IntPtr.Size),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
            }

            completed = true;
            pin = hold;
            return list;
        }
        finally
        {
            if (!completed)
            {
                if (hold.IsAllocated)
                {
                    hold.Free();
                }

                Marshal.FreeHGlobal(list);
                pin = default;
            }
        }
    }

    private static ServiceContender SpawnUnix(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        Dictionary<string, string?> environment,
        string? redaction)
    {
        var arguments = new List<string>(startInfo.Arguments.Count + 1) { startInfo.Executable.Path };
        arguments.AddRange(startInfo.Arguments);
        var variables = new List<string>(environment.Count);
        variables.AddRange(environment
            .Where(entry => entry.Value is not null)
            .Select(entry => entry.Key + "=" + entry.Value));

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

    /// <summary>Names the resolved target alongside the caller's spelling, when they differ — the launcher's failure posture, without any secret-bearing value.</summary>
    private static OpenCodeServerException SpawnFailure(
        IServiceContenderSpawner.ContenderStartInfo startInfo, Exception cause)
    {
        var command = startInfo.Executable.Command;
        var detail = string.Equals(command, startInfo.Executable.Path, StringComparison.Ordinal)
            ? command
            : command + " (resolved to '" + startInfo.Executable.Path + "')";
        return new OpenCodeServerException(
            $"Failed to start the background service contender '{detail}'.", cause);
    }

    private static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint StructureSize;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public short ShowWindow;
        public short ReservedSize;
        public IntPtr ReservedData;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Startup;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr ProcessThread;
        public uint ProcessId;
        public uint ThreadId;
    }

#if NET
    [LibraryImport("kernel32", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        string? applicationName,
        IntPtr commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        string? environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr OpenNullDevice(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        ref SecurityAttributes attributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32", EntryPoint = "CloseHandle", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32", EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeAttributeList(IntPtr list, int attributeCount, uint flags, ref IntPtr size);

    [LibraryImport("kernel32", EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateAttribute(
        IntPtr list,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [LibraryImport("kernel32", EntryPoint = "DeleteProcThreadAttributeList")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void DeleteAttributeList(IntPtr list);

    [LibraryImport("libc", EntryPoint = "sigfillset", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int FillSignalSet(IntPtr set);

    [LibraryImport("libc", EntryPoint = "sigemptyset", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int EmptySignalSet(IntPtr set);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setsigdefault")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SetDefaultSignals(IntPtr attributes, IntPtr set);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setsigmask")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SetSignalMask(IntPtr attributes, IntPtr set);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int InitAttributes(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SetAttributeFlags(IntPtr attributes, short flags);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int DestroyAttributes(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int InitFileActions(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addopen", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int AddOpenFileAction(IntPtr actions, int descriptor, string path, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int AddDuplicateAction(IntPtr actions, int descriptor, int newDescriptor);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int DestroyFileActions(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawnp", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SpawnProcess(
        out int pid,
        string file,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr argv,
        IntPtr envp);
#else
    [DllImport("kernel32", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? applicationName,
        IntPtr commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        string? environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenNullDevice(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        ref SecurityAttributes attributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32", EntryPoint = "CloseHandle", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32", EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeAttributeList(IntPtr list, int attributeCount, uint flags, ref IntPtr size);

    [DllImport("kernel32", EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateAttribute(
        IntPtr list,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32", EntryPoint = "DeleteProcThreadAttributeList")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void DeleteAttributeList(IntPtr list);

    [DllImport("libc", EntryPoint = "sigfillset", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int FillSignalSet(IntPtr set);

    [DllImport("libc", EntryPoint = "sigemptyset", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int EmptySignalSet(IntPtr set);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setsigdefault")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SetDefaultSignals(IntPtr attributes, IntPtr set);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setsigmask")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SetSignalMask(IntPtr attributes, IntPtr set);

    [DllImport("libc", EntryPoint = "posix_spawnattr_init")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int InitAttributes(IntPtr attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SetAttributeFlags(IntPtr attributes, short flags);

    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int DestroyAttributes(IntPtr attributes);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int InitFileActions(IntPtr actions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int AddOpenFileAction(IntPtr actions, int descriptor, [MarshalAs(UnmanagedType.LPStr)] string path, int flags, int mode);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int AddDuplicateAction(IntPtr actions, int descriptor, int newDescriptor);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int DestroyFileActions(IntPtr actions);

    [DllImport("libc", EntryPoint = "posix_spawnp", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SpawnProcess(
        out int pid,
        [MarshalAs(UnmanagedType.LPStr)] string file,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr argv,
        IntPtr envp);
#endif
}
