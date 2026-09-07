using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

internal sealed class DiagnosticFileWriter(IFileSystem fileSystem)
{
    public async Task WriteAsync(string path, string text)
    {
        using var writer = fileSystem.File.CreateText(path);
        await writer.WriteAsync(text);
    }
}
