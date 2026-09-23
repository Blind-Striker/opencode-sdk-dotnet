using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using static OpenCode.Sdk.Internal.BackgroundService.BackgroundServiceInterop.Kernel32;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>The Windows arm: <c>CreateProcessW</c> with <c>DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP</c>, NUL stdin and stdout, the stderr pipe, and an explicit inherited-handle list.</summary>
internal sealed partial class ServiceContenderSpawner
{
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

    /// <summary><c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>: only these handles cross into the child.</summary>
    private static readonly IntPtr HandleListAttribute = new(0x20002);

    /// <summary><c>INVALID_HANDLE_VALUE</c>, what <c>CreateFileW</c> returns on failure.</summary>
    private static readonly IntPtr InvalidHandle = new(-1);

    private static ServiceContender SpawnWindows(
        IServiceContenderSpawner.ContenderStartInfo startInfo,
        Dictionary<string, string> environment,
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
}
