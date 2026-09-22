using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The shipped <see cref="IServiceContenderSpawner"/> on the platform's own spawn primitives
/// (design 6.3). The modern targets take <c>LibraryImport</c>, the compile-time stub .NET
/// recommends; the <c>netstandard2.0</c> asset, where that generator is unavailable, takes the
/// equivalent <c>DllImport</c> — the <c>ServiceProcessControl</c> precedent, Mono included.
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

    /// <summary><c>F_SETFD</c> / <c>FD_CLOEXEC</c>: the stderr pipe never survives an unrelated exec.</summary>
    private const int SetCloseOnExecCommand = 2;

    private const int CloseOnExec = 1;

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
            return SpawnWindowsChild(startInfo, commandLine, BuildEnvironmentBlock(environment), redaction);
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
    /// The Windows spawn keeps every native handle as an <c>IntPtr</c> until ownership moves: the
    /// attribute list needs raw values, and wrapping earlier would force the handle back out
    /// through the dangerous door. Anything still raw in the finally below is closed there.
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

        var read = IntPtr.Zero;
        var write = IntPtr.Zero;
        var nul = IntPtr.Zero;
        SafeFileHandle? readHandle = null;
        SafeProcessHandle? process = null;
        try
        {
            if (!CreatePipe(out read, out write, ref security, 0))
            {
                throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
            }

            nul = OpenNullDevice(
                "NUL", GenericRead | GenericWrite, ShareReadWrite, ref security, OpenExisting, 0, IntPtr.Zero);
            if (nul == IntPtr.Zero || nul == new IntPtr(-1))
            {
                throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
            }

            var inherited = new[] { nul, write };
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

                readHandle = new SafeFileHandle(read, ownsHandle: true);
                read = IntPtr.Zero;
                var contender = new ServiceContender((int)info.ProcessId, readHandle, process, redaction);
                readHandle = null;
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
            if (read != IntPtr.Zero)
            {
                _ = CloseHandle(read);
            }

            if (write != IntPtr.Zero)
            {
                _ = CloseHandle(write);
            }

            if (nul != IntPtr.Zero)
            {
                _ = CloseHandle(nul);
            }

            readHandle?.Dispose();
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

    private static ServiceContender SpawnUnixChild(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        IntPtr argv,
        IntPtr envp,
        string? redaction)
    {
        AllocatePipe(startInfo, out var readEnd, out var writeEnd);
        MarkCloseOnExec(startInfo, readEnd, writeEnd);

        var actions = new byte[FileActionsCapacity];
        var attributes = new byte[SpawnAttributesCapacity];
        var actionsHandle = GCHandle.Alloc(actions, GCHandleType.Pinned);
        var attributesHandle = GCHandle.Alloc(attributes, GCHandleType.Pinned);
        SafeFileHandle? read = null;
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
                    CheckNativeResult(startInfo, SetAttributeFlags(attributesPtr, SpawnFlags()));
                    CheckNativeResult(startInfo, AddOpenFileAction(actionsPtr, 0, "/dev/null", ReadOnly, 0));
                    CheckNativeResult(startInfo, AddOpenFileAction(actionsPtr, 1, "/dev/null", WriteOnly, 0));
                    CheckNativeResult(startInfo, AddDuplicateAction(actionsPtr, writeEnd, 2));
                    CheckNativeResult(startInfo, AddCloseAction(actionsPtr, readEnd));

                    // posix_spawn reports the errno as its return value rather than through the
                    // thread's errno, so the value below — not GetLastWin32Error — is the failure.
                    var spawned = SpawnProcess(out var pid, startInfo.Executable.Path, actionsPtr, attributesPtr, argv, envp);

                    // The parent never holds the write end: keeping it would keep EOF away after
                    // every writer is gone.
                    _ = CloseDescriptor(writeEnd);
                    writeEnd = -1;
                    if (spawned != 0)
                    {
                        throw SpawnFailure(startInfo, new Win32Exception(spawned));
                    }

                    read = new SafeFileHandle(new IntPtr(readEnd), ownsHandle: true);
                    readEnd = -1;
                    var contender = new ServiceContender(pid, read, null, redaction);
                    read = null;
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
            if (readEnd >= 0)
            {
                _ = CloseDescriptor(readEnd);
            }

            if (writeEnd >= 0)
            {
                _ = CloseDescriptor(writeEnd);
            }

            read?.Dispose();
        }
    }

    /// <summary>
    /// The two pipe ends as raw descriptors: the source-generated interop cannot size an array
    /// argument, so the pair crosses through two descriptors rather than one array.
    /// </summary>
    private static void AllocatePipe(IServiceContenderSpawner.ContenderStartInfo startInfo, out int readEnd, out int writeEnd)
    {
        var descriptors = Marshal.AllocHGlobal(2 * sizeof(int));
        try
        {
            // pipe(2) reports 0 for success, so the declaration stays an int: a BOOL
            // marshal would read every successful call as a failure.
            if (Pipe(descriptors) != 0)
            {
                throw SpawnFailure(startInfo, new Win32Exception(Marshal.GetLastWin32Error()));
            }

            readEnd = Marshal.ReadInt32(descriptors, 0);
            writeEnd = Marshal.ReadInt32(descriptors, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(descriptors);
        }
    }

    /// <summary>
    /// The pipe ends must not survive an unrelated exec racing this spawn: a leaked write end in
    /// another child would hold EOF away from this contender's drain.
    /// </summary>
    private static void MarkCloseOnExec(IServiceContenderSpawner.ContenderStartInfo startInfo, int first, int second)
    {
        if (ControlDescriptor(first) && ControlDescriptor(second))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        _ = CloseDescriptor(first);
        _ = CloseDescriptor(second);
        throw SpawnFailure(startInfo, new Win32Exception(error));
    }

    private static bool ControlDescriptor(int descriptor) =>
        SetCloseOnExec(descriptor, SetCloseOnExecCommand, CloseOnExec) == 0;

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

    /// <summary>
    /// The <c>CreateProcessW</c> Unicode block: NUL-separated <c>key=value</c> pairs with one extra
    /// NUL ending it. The interop layer copies the managed length plus its terminator, so the
    /// embedded separators survive exactly as built.
    /// </summary>
    private static string BuildEnvironmentBlock(Dictionary<string, string?> environment)
    {
        var block = new StringBuilder();
        foreach (var entry in environment)
        {
            if (entry.Value is null)
            {
                continue;
            }

            _ = block.Append(entry.Key).Append('=').Append(entry.Value).Append('\0');
        }

        return block.Append('\0').ToString();
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

    [LibraryImport("kernel32", EntryPoint = "CreatePipe", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SecurityAttributes attributes,
        uint size);

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

    [LibraryImport("libc", EntryPoint = "pipe", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Pipe(IntPtr descriptors);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SetCloseOnExec(int descriptor, int command, int value);

    [LibraryImport("libc", EntryPoint = "close")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int CloseDescriptor(int descriptor);

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

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int AddCloseAction(IntPtr actions, int descriptor);

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

    [DllImport("kernel32", EntryPoint = "CreatePipe", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SecurityAttributes attributes,
        uint size);

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

    [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Pipe(IntPtr descriptors);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SetCloseOnExec(int descriptor, int command, int value);

    [DllImport("libc", EntryPoint = "close")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int CloseDescriptor(int descriptor);

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

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int AddCloseAction(IntPtr actions, int descriptor);

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
