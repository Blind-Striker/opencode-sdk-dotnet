namespace OpenCode.Sdk.Extensions.Tests.Support;

/// <summary>Canned configuration sections for the binding tests.</summary>
internal static class OpenCodeConfigurationData
{
    /// <summary>A password-protected server with a non-default username.</summary>
    public static IReadOnlyDictionary<string, string?> ProtectedServer { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["OpenCode:Endpoint"] = "http://localhost:4096",
            ["OpenCode:Username"] = "admin",
            ["OpenCode:Password"] = "secret",
        };
}
