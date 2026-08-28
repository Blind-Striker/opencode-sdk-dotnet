using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Deletes a temporary directory tree without letting the deletion itself end a test run.
/// <see cref="TestRunRoot"/> and <see cref="TestWorkspace"/> both dispose directories a launched
/// server was writing into moments earlier, so losing the race against a straggling child handle
/// is expected rather than exceptional. Only that contention is absorbed: the failure is reported
/// as a false result, and anything else the file system raises is a real defect and propagates.
/// </summary>
internal static class BestEffortDelete
{
    /// <summary>Deletes the directory tree at <paramref name="path"/>, contents included.</summary>
    /// <returns>
    /// True when the tree is gone; false when a live handle or an access denial kept it, in which
    /// case the operating system's temp cleaner owns the leftovers.
    /// </returns>
    public static bool TryDeleteTree(IFileSystem fileSystem, string path)
    {
        try
        {
            fileSystem.Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException)
        {
            // The tree, or a file inside it, is still open: a child process outlived the fixture
            // that owned it, which retention makes harmless.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The same contention surfaces as an access denial on some platforms, and a read-only
            // entry written into the tree reports it too.
            return false;
        }
    }
}
