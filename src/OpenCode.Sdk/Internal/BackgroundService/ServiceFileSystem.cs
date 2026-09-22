#if !NET
using System.Runtime.InteropServices;
#endif
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The shipped <see cref="IServiceFileSystem"/> over <c>System.IO</c>. The exclusive create mirrors
/// the pinned CLI's migration write (<c>flag: "wx", mode: 0o600</c>): on the modern targets Unix
/// gets an atomic owner-only create through <c>FileStreamOptions.UnixCreateMode</c>; on the
/// <c>netstandard2.0</c> asset the file is created first and its mode set second through the
/// polyfilled <c>File.SetUnixFileMode</c>, a best-effort <c>chmod</c> the roadmap records as a known
/// gap; Windows inherits the parent directory's DACL, as Node's <c>mode</c> does there.
/// </summary>
internal sealed class ServiceFileSystem : IServiceFileSystem
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public bool FileExists(string path) => File.Exists(path);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    public Task<bool> TryCreateExclusiveAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateExclusive(path, bytes));
    }

    public bool TryDelete(string path)
    {
        // File.Exists is false for a missing directory as well as a missing file, which keeps
        // Delete's DirectoryNotFoundException off this path; the pinned client's rm({force:true})
        // reads both as "nothing to remove".
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public void Rename(string source, string destination)
    {
#if NET
        // The modern targets replace the destination in one native move (rename(2) on Unix,
        // MoveFileEx with MOVEFILE_REPLACE_EXISTING on Windows), which is the pinned client's
        // atomic sidecar publication.
        File.Move(source, destination, overwrite: true);
#else
        // net472 and netstandard2.0 have no overwrite overload. File.Replace swaps an existing
        // destination in one step (ReplaceFile on Windows, rename(2) under Mono), so no reader ever
        // sees the destination missing and a failed replace leaves it untouched; a destination that
        // does not exist yet is a plain move. A destination removed between the two looks fails the
        // replace, which the publisher reports and the election retries.
        if (File.Exists(destination))
        {
            File.Replace(source, destination, destinationBackupFileName: null);
            return;
        }

        File.Move(source, destination);
#endif
    }

    /// <summary>
    /// A registration is a few hundred bytes, so the create, the mode, the write, and the close run
    /// synchronously inside one <c>using</c>: nothing interleaves with a half-written file, and the
    /// partial-file cleanup stays one catch.
    /// </summary>
    private static bool CreateExclusive(string path, ReadOnlyMemory<byte> bytes)
    {
        var created = false;
        try
        {
            using var stream = OpenExclusive(path);
            created = true;
#if !NET
            SetOwnerOnlyMode(path);
#endif
            stream.Write(bytes.Span);
            return true;
        }
        catch (IOException) when (!created && File.Exists(path))
        {
            // CreateNew refused because the target exists: the caller's "someone else wrote it
            // first" outcome, never an error, and the existing file is untouched.
            return false;
        }
        catch when (created)
        {
            // A partial file this call created must not survive as a registration candidate.
            _ = TryRemovePartialFile(path);
            throw;
        }
    }

#if NET
    private static FileStream OpenExclusive(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        return new FileStream(path, options);
    }
#else
    private static FileStream OpenExclusive(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    private static void SetOwnerOnlyMode(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        File.SetUnixFileMode(path, OwnerOnly);
    }
#endif

    private static bool TryRemovePartialFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            // The original failure is the one worth reporting.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Same: the create failed for a reason the caller already sees.
            return false;
        }
    }
}
