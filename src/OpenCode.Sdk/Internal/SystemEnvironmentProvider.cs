using OpenCode.Sdk.Internal.Abstractions;

namespace OpenCode.Sdk.Internal;

/// <summary>Reads environment variables from the current process.</summary>
internal sealed class SystemEnvironmentProvider : IEnvironmentProvider
{
    public string? GetEnvironmentVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Environment.GetEnvironmentVariable(name);
    }
}
