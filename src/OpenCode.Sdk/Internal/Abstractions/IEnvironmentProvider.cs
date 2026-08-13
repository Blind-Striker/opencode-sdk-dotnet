namespace OpenCode.Sdk.Internal.Abstractions;

/// <summary>Reads process environment variables behind a substitutable seam.</summary>
internal interface IEnvironmentProvider
{
    /// <summary>Reads one environment variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <see langword="null"/> when the variable is not set.</returns>
    public string? GetEnvironmentVariable(string name);
}
