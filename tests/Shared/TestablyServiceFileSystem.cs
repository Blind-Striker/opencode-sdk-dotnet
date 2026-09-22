using System.IO.Abstractions;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The test implementation of the SDK's background-service filesystem seam: every member forwards
/// to the injected <see cref="IFileSystem"/>, so <c>MockFileSystem</c> stands in at levels 1-2 and
/// <c>RealFileSystem</c> at level 3 and the repository keeps one filesystem double. It makes no
/// file-mode claim; the shipped implementation's mode behavior is proven against the real
/// filesystem by its own tests.
/// </summary>
internal sealed class TestablyServiceFileSystem(IFileSystem fileSystem) : IServiceFileSystem
{
    public bool FileExists(string path) => fileSystem.File.Exists(path);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fileSystem.File.ReadAllBytes(path));
    }

    public async Task<bool> TryCreateExclusiveAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (fileSystem.File.Exists(path))
        {
            return false;
        }

        using var stream = fileSystem.FileStream.New(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public bool TryDelete(string path)
    {
        if (!fileSystem.File.Exists(path))
        {
            return false;
        }

        fileSystem.File.Delete(path);
        return true;
    }

    public void Rename(string source, string destination)
    {
        // The netstandard2.0 Testably asset has only the two-argument Move, so the adapter uses the
        // same delete-then-move arm the shipped downlevel implementation does; it makes no
        // atomicity claim, matching its existing no-file-mode-claim posture.
        if (fileSystem.File.Exists(destination))
        {
            fileSystem.File.Delete(destination);
        }

        fileSystem.File.Move(source, destination);
    }
}
