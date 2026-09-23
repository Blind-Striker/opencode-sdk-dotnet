using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// Every native function the background-service door binds, in one place: the Windows process
/// API and the C library's spawn, signal, and wait calls (ADR-0026, ADR-0027). The modern targets
/// take <c>LibraryImport</c>, the compile-time stub .NET recommends (its generator is what needs
/// the project's unsafe-code switch); the <c>netstandard2.0</c> asset, where that generator is
/// unavailable, takes the equivalent <c>DllImport</c>. "libc" is the portable spelling:
/// <c>libSystem.Native</c>'s loader maps it to the platform's own C library instead of probing for
/// a file, under CoreCLR and native AOT alike.
/// </summary>
internal static partial class BackgroundServiceInterop
{
    /// <summary>Gets a value indicating whether the process runs on Windows, on every target framework.</summary>
    public static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

    /// <summary>The Windows process API.</summary>
    internal static partial class Kernel32
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes
        {
            public uint Length;
            public IntPtr SecurityDescriptor;
            public int InheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StartupInfo
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
        internal struct StartupInfoEx
        {
            public StartupInfo Startup;
            public IntPtr AttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
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
        internal static partial bool CreateProcess(
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
        internal static partial IntPtr OpenNullDevice(
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
        internal static partial bool CloseHandle(IntPtr handle);

        [LibraryImport("kernel32", EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool InitializeAttributeList(IntPtr list, int attributeCount, uint flags, ref IntPtr size);

        [LibraryImport("kernel32", EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UpdateAttribute(
            IntPtr list,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            IntPtr size,
            IntPtr previousValue,
            IntPtr returnSize);

        [LibraryImport("kernel32", EntryPoint = "DeleteProcThreadAttributeList")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial void DeleteAttributeList(IntPtr list);

        [LibraryImport("kernel32", EntryPoint = "GetExitCodeProcess", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
#else
        [DllImport("kernel32", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
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
        internal static extern IntPtr OpenNullDevice(
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
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32", EntryPoint = "InitializeProcThreadAttributeList", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeAttributeList(IntPtr list, int attributeCount, uint flags, ref IntPtr size);

        [DllImport("kernel32", EntryPoint = "UpdateProcThreadAttribute", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateAttribute(
            IntPtr list,
            uint flags,
            IntPtr attribute,
            IntPtr value,
            IntPtr size,
            IntPtr previousValue,
            IntPtr returnSize);

        [DllImport("kernel32", EntryPoint = "DeleteProcThreadAttributeList")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void DeleteAttributeList(IntPtr list);

        [DllImport("kernel32", EntryPoint = "GetExitCodeProcess", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
#endif
    }

    /// <summary>The C library.</summary>
    internal static partial class Libc
    {
#if NET
        [LibraryImport("libc", EntryPoint = "sigfillset", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int FillSignalSet(IntPtr set);

        [LibraryImport("libc", EntryPoint = "sigemptyset", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int EmptySignalSet(IntPtr set);

        [LibraryImport("libc", EntryPoint = "posix_spawnattr_setsigdefault")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SetDefaultSignals(IntPtr attributes, IntPtr set);

        [LibraryImport("libc", EntryPoint = "posix_spawnattr_setsigmask")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SetSignalMask(IntPtr attributes, IntPtr set);

        [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int InitAttributes(IntPtr attributes);

        [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SetAttributeFlags(IntPtr attributes, short flags);

        [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int DestroyAttributes(IntPtr attributes);

        [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int InitFileActions(IntPtr actions);

        [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addopen", StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int AddOpenFileAction(IntPtr actions, int descriptor, string path, int flags, int mode);

        [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int AddDuplicateAction(IntPtr actions, int descriptor, int newDescriptor);

        [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int DestroyFileActions(IntPtr actions);

        [LibraryImport("libc", EntryPoint = "posix_spawnp", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SpawnProcess(
            out int pid,
            string file,
            IntPtr fileActions,
            IntPtr attributes,
            IntPtr argv,
            IntPtr envp);

        [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int WaitPid(int pid, out int status, int options);

        /// <summary>
        /// <c>kill(2)</c>: two blittable integers, so there is no marshalling to generate on either
        /// form. The modern targets take <c>LibraryImport</c>, the compile-time stub .NET recommends
        /// (its generator is what needs the project's unsafe-code switch); the <c>netstandard2.0</c>
        /// asset, where that generator is unavailable, takes the equivalent <c>DllImport</c>. "libc" is
        /// the portable spelling: <c>libSystem.Native</c>'s loader maps it to the platform's own C
        /// library (<c>libc.so.6</c>, <c>/usr/lib/libc.dylib</c>) instead of probing for a file, under
        /// CoreCLR and native AOT alike.
        /// </summary>
        [LibraryImport("libc", EntryPoint = "kill")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Kill(int processId, int signal);
#else
        [DllImport("libc", EntryPoint = "sigfillset", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int FillSignalSet(IntPtr set);

        [DllImport("libc", EntryPoint = "sigemptyset", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int EmptySignalSet(IntPtr set);

        [DllImport("libc", EntryPoint = "posix_spawnattr_setsigdefault")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int SetDefaultSignals(IntPtr attributes, IntPtr set);

        [DllImport("libc", EntryPoint = "posix_spawnattr_setsigmask")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int SetSignalMask(IntPtr attributes, IntPtr set);

        [DllImport("libc", EntryPoint = "posix_spawnattr_init")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int InitAttributes(IntPtr attributes);

        [DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int SetAttributeFlags(IntPtr attributes, short flags);

        [DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int DestroyAttributes(IntPtr attributes);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int InitFileActions(IntPtr actions);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int AddOpenFileAction(IntPtr actions, int descriptor, [MarshalAs(UnmanagedType.LPStr)] string path, int flags, int mode);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int AddDuplicateAction(IntPtr actions, int descriptor, int newDescriptor);

        [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int DestroyFileActions(IntPtr actions);

        [DllImport("libc", EntryPoint = "posix_spawnp", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int SpawnProcess(
            out int pid,
            [MarshalAs(UnmanagedType.LPStr)] string file,
            IntPtr fileActions,
            IntPtr attributes,
            IntPtr argv,
            IntPtr envp);

        [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int WaitPid(int pid, out int status, int options);

        [DllImport("libc", EntryPoint = "kill")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static extern int Kill(int processId, int signal);
#endif
    }
}
