using System.IO.Abstractions;
using OpenCode.Sdk.TestSupport.Ownership.Abstractions;

namespace OpenCode.Sdk.TestSupport;

internal sealed record ServerFixtureDiagnosticsOptions
{
    public required IFileSystem FileSystem { get; init; }

    public required string ResultsDirectory { get; init; }

    public IOwnedOperationDeadline? Deadline { get; init; }
}
