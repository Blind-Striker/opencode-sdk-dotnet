using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Per-run home/state isolation for a spawned pinned server, in the shape of upstream's own test
/// fixture (<c>packages/cli/test/fixture/environment.ts</c> at the pin): every global root the
/// server resolves comes from these variables (<c>global-roots.ts:5-8</c>), the two variables
/// that would otherwise redirect state to the developer's real files are pinned explicitly
/// rather than left to inheritance (<c>OPENCODE_CONFIG_DIR</c> replaces the whole config root,
/// <c>global.ts:79</c>; <c>OPENCODE_DB</c> replaces the database path, <c>database-path.ts:6</c>),
/// the config seed is empty (upstream's hermetic posture), and the models catalog fetch is off so
/// no unregistered outbound network rides the suite (ADR-0022). A child inherits everything else
/// from the runner, so this map is the isolation boundary: a variable that can steer a server at
/// the developer's data belongs here, set, not merely absent.
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
            // The same directory XDG_CONFIG_HOME would resolve to, named explicitly: the variable
            // carries the whole config root, so a fixture that seeds a service config under
            // <config>/opencode/ and the daemon that reads it agree by construction.
            ["OPENCODE_CONFIG_DIR"] = fileSystem.Path.Combine(runRoot, "config", "opencode"),
            ["OPENCODE_DB"] = fileSystem.Path.Combine(runRoot, "data", "opencode.db"),
            ["OPENCODE_CONFIG_CONTENT"] = "{}",
            ["OPENCODE_DISABLE_MODELS_FETCH"] = "1",
        };

    /// <summary>
    /// The XDG map plus a redirected home: service mode changes into <c>global.home</c> before it
    /// serves (<c>packages/cli/src/server-process.ts:55</c> with <c>packages/util/src/global.ts:17</c>
    /// at the pin), and upstream's own fixture sets all three variables
    /// (<c>packages/cli/test/fixture/environment.ts</c>). The home is <c>&lt;runRoot&gt;/home</c>;
    /// the caller creates it.
    /// </summary>
    public static Dictionary<string, string> HomeAwareEnvironment(IFileSystem fileSystem, string runRoot)
    {
        var environment = Environment(fileSystem, runRoot);
        var home = fileSystem.Path.Combine(runRoot, "home");
        environment["OPENCODE_TEST_HOME"] = home;
        environment["HOME"] = home;
        if (OperatingSystem.IsWindows())
        {
            environment["USERPROFILE"] = home;
        }

        return environment;
    }
}
