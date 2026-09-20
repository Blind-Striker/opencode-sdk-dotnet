using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// One registration file as the pinned client's <c>read</c> sees it: a file that is missing,
/// unreadable, or undecodable is absent state, never a failure (<c>effect/service.ts</c>, "a
/// missing or corrupt file means no valid info; callers treat both the same"). Discovery,
/// migration, and stop all read through here so the folding happens once.
/// </summary>
internal sealed class ServiceRegistrationFile(IServiceFileSystem fileSystem)
{
    /// <summary>Reads and decodes the registration at the path.</summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="cancellationToken">The caller's token; cancellation is the one failure that propagates.</param>
    /// <returns>The registration, or null for anything the daemon could not have written.</returns>
    public async Task<ServiceRegistration?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await TryReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : ServiceRegistrationReader.TryRead(bytes);
    }

    /// <summary>Reads the file's bytes, folding a missing or unreadable file into null.</summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="cancellationToken">The caller's token; cancellation is the one failure that propagates.</param>
    /// <returns>The bytes, or null.</returns>
    public async Task<byte[]?> TryReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            return await fileSystem.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
