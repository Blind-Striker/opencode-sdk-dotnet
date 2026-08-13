using System.Net.Http.Headers;
using System.Reflection;

namespace OpenCode.Sdk.Internal;

/// <summary>Composes the SDK's User-Agent product token.</summary>
internal static class UserAgentPolicy
{
    private const string ProductName = "OpenCode.Sdk";

    /// <summary>Reads the SDK assembly's informational version into the product token.</summary>
    public static ProductInfoHeaderValue Resolve() =>
        Compose(typeof(UserAgentPolicy).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Composes the product token from an informational version. Build metadata after
    /// <c>+</c> is stripped; a missing or unparsable version omits the version token
    /// entirely — never a silent substitute, never a construction failure
    /// (maintainer, 2026-08-13).
    /// </summary>
    /// <param name="informationalVersion">The assembly's informational version, when present.</param>
    /// <returns>The product token for User-Agent decoration.</returns>
    public static ProductInfoHeaderValue Compose(string? informationalVersion)
    {
        var version = informationalVersion?.Split('+')[0].Trim();
        return !string.IsNullOrEmpty(version) && ProductHeaderValue.TryParse($"{ProductName}/{version}", out var parsed)
            ? new ProductInfoHeaderValue(parsed)
            : new ProductInfoHeaderValue(new ProductHeaderValue(ProductName));
    }
}
