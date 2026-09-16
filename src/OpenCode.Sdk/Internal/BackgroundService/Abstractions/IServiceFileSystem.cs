namespace OpenCode.Sdk.Internal.BackgroundService.Abstractions;

/// <summary>
/// The file access the background-service door performs, and nothing more: probe, read, and one
/// exclusive create. The shipped implementation sits on <c>System.IO</c>; tests supply an adapter
/// over the repository's filesystem double.
/// </summary>
internal interface IServiceFileSystem
{
    /// <summary>Reports whether a file exists at the path.</summary>
    /// <param name="path">The absolute path.</param>
    /// <returns>True when a file is present.</returns>
    public bool FileExists(string path);

    /// <summary>Reads a whole file.</summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The file's bytes.</returns>
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a file that must not exist yet, with owner-only access where the platform can
    /// express it, and writes the bytes. An existing target is reported, never replaced; every
    /// other failure throws and leaves no partial file behind.
    /// </summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>True when the file was created; false when the target already existed.</returns>
    public Task<bool> TryCreateExclusiveAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}
