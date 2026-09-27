using System.Runtime.InteropServices;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// The Windows functions the launcher's output readers bind to cancel a synchronous pipe read on
/// their own dedicated thread: there is no managed API that cancels synchronous I/O another thread
/// is blocked in. The modern targets take <c>LibraryImport</c>; the <c>net472</c> and
/// <c>netstandard2.0</c> assets take the equivalent <c>DllImport</c> (ADR-0026, ADR-0027).
/// </summary>
internal static partial class LauncherInterop
{
    /// <summary><c>THREAD_TERMINATE</c>: the access right <c>CancelSynchronousIo</c> requires.</summary>
    internal const uint ThreadTerminate = 0x0001;

    /// <summary>Gets a value indicating whether the process runs on Windows, on every target framework.</summary>
    public static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

    /// <summary>The Windows thread API.</summary>
    internal static partial class Kernel32
    {
#if NET
        [LibraryImport("kernel32", EntryPoint = "GetCurrentThreadId")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial uint GetCurrentThreadId();

        [LibraryImport("kernel32", EntryPoint = "OpenThread", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial IntPtr OpenThread(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint threadId);

        [LibraryImport("kernel32", EntryPoint = "CancelSynchronousIo", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CancelSynchronousIo(IntPtr thread);

        [LibraryImport("kernel32", EntryPoint = "CloseHandle", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);
#else
        [DllImport("kernel32", EntryPoint = "GetCurrentThreadId")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint GetCurrentThreadId();

        [DllImport("kernel32", EntryPoint = "OpenThread", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr OpenThread(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint threadId);

        [DllImport("kernel32", EntryPoint = "CancelSynchronousIo", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CancelSynchronousIo(IntPtr thread);

        [DllImport("kernel32", EntryPoint = "CloseHandle", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
#endif
    }
}
