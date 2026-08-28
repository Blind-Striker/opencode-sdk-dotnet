using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Per-run home/state isolation for a spawned pinned server: every global root the server
/// resolves comes from these variables (global-roots.ts:5-8 at the pin), the config seed is
/// empty (upstream's own hermetic fixture posture), and the models catalog fetch is off so no
/// unregistered outbound network rides the suite (ADR-0022).
/// </summary>
internal static class ServerIsolation
{
    public static Dictionary<string, string> Environment(IFileSystem fileSystem, string runRoot) =>
        new(StringComparer.Ordinal)
        {
            ["XDG_DATA_HOME"] = fileSystem.Path.Combine(runRoot, "data"),
            ["XDG_CACHE_HOME"] = fileSystem.Path.Combine(runRoot, "cache"),
            ["XDG_CONFIG_HOME"] = fileSystem.Path.Combine(runRoot, "config"),
            ["XDG_STATE_HOME"] = fileSystem.Path.Combine(runRoot, "state"),
            ["OPENCODE_CONFIG_CONTENT"] = "{}",
            ["OPENCODE_DISABLE_MODELS_FETCH"] = "1",
        };
}
