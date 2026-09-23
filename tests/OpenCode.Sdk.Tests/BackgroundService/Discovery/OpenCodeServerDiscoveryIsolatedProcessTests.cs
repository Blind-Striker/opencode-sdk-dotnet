using System.Globalization;
using System.Net;
using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService.Discovery;

/// <summary>
/// The environment reading of <c>DiscoverAsync</c>, proven from a process whose environment the
/// test owns entirely (the isolated discovery executable): the default shared registration under
/// <c>XDG_STATE_HOME</c>, the home-directory fallback with no XDG root at all, and a stale
/// registration answered without waiting out the bound. The daemon each child probes is a loopback
/// health server hosted in this test process, so no test reads the developer's profile or
/// registration.
/// </summary>
/// <remarks>
/// Keyless <c>[NotInParallel]</c>, the rule research log Q157 sets for a test whose assertion
/// depends on a wall-clock bound the host can miss under load: the child's probe is bounded at the
/// pinned two seconds, and the server it probes lives in this process, so a test host busy with
/// the rest of the suite can starve the loopback server past that bound. Two CI runs on the
/// Windows leg showed exactly that (children of 3 to 7 seconds answering "missing") while the
/// sibling that probes the real daemon in another process never did. Running these alone after
/// every other test keeps the host quiet while the child probes.
/// </remarks>
[NotInParallel]
public sealed class OpenCodeServerDiscoveryIsolatedProcessTests
{
    private const string IsolatedPassword = "isolated-p455";
    private static readonly RealFileSystem FileSystem = new();

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Use_The_Default_Shared_Registration(CancellationToken cancellationToken)
    {
        using var root = new TestRunRoot(FileSystem);
        await using var health = LoopbackHttpServer.Start(static _ => ReadyHealth());
        var state = FileSystem.Path.Combine(root.Path, "state");
        Seed(FileSystem.Path.Combine(state, "opencode", "service.json"), health.Endpoint);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_STATE_HOME"] = state,
            ["XDG_CONFIG_HOME"] = FileSystem.Path.Combine(root.Path, "config"),
            ["OPENCODE_CONFIG_DIR"] = null,
        };

        var result = await new ServiceFixtureCommand(FileSystem).RunAsync(["discover-default"], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim())
            .IsEqualTo(ServiceFixtureOutput.FoundLine(ServiceInfoBodyData.Pid, health.Endpoint))
            .Because(result.StandardError);
        await Assert.That(health.RequestPaths).IsEquivalentTo(["/api/info"]);
    }

    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Fall_Back_To_The_Home_Directory(CancellationToken cancellationToken)
    {
        using var root = new TestRunRoot(FileSystem);
        await using var health = LoopbackHttpServer.Start(static _ => ReadyHealth());
        var home = FileSystem.Path.Combine(root.Path, "home");
        Seed(FileSystem.Path.Combine(home, ".local", "state", "opencode", "service.json"), health.Endpoint);
        // No XDG state root at all: the only way to the registration is the redirected home,
        // read through USERPROFILE on Windows and HOME elsewhere (the libuv rule the SDK follows).
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_STATE_HOME"] = null,
            ["XDG_CONFIG_HOME"] = null,
            ["OPENCODE_CONFIG_DIR"] = null,
            ["HOME"] = home,
            ["USERPROFILE"] = home,
        };

        var result = await new ServiceFixtureCommand(FileSystem).RunAsync(["discover-default"], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim())
            .IsEqualTo(ServiceFixtureOutput.FoundLine(ServiceInfoBodyData.Pid, health.Endpoint))
            .Because(result.StandardError);
        await Assert.That(health.RequestPaths).IsEquivalentTo(["/api/info"]);
    }

    /// <summary>
    /// A registration whose daemon is gone (the port is closed) answers "missing" at once, not at
    /// the two-second bound: a refused loopback connect must classify as no service on every
    /// host, including Windows, where the default connect retransmits the SYN for about two
    /// seconds. The child is the real SDK, so this proves the probe's transport end to end.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task DiscoverAsync_Should_Report_A_Stale_Registration_Without_Waiting_For_The_Bound(CancellationToken cancellationToken)
    {
        using var root = new TestRunRoot(FileSystem);
        Uri stale;
        await using (var gone = LoopbackHttpServer.Start(static _ => ReadyHealth()))
        {
            stale = gone.Endpoint;
        }

        var state = FileSystem.Path.Combine(root.Path, "state");
        Seed(FileSystem.Path.Combine(state, "opencode", "service.json"), stale);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_STATE_HOME"] = state,
            ["XDG_CONFIG_HOME"] = FileSystem.Path.Combine(root.Path, "config"),
            ["OPENCODE_CONFIG_DIR"] = null,
        };

        var result = await new ServiceFixtureCommand(FileSystem).RunAsync(["discover-default"], environment, cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo("missing").Because(result.StandardError);
        var elapsed = ServiceFixtureOutput.DiscoveryMilliseconds(result.StandardError);
        await Assert.That(elapsed).IsNotNull().Because(result.StandardError);
        await Assert.That(elapsed!.Value).IsLessThan(1_000).Because(result.StandardError);
    }

    private static LoopbackHttpResponse ReadyHealth() =>
        new() { StatusCode = HttpStatusCode.OK, Body = ServiceInfoBodyData.Ready, ContentType = "application/json" };

    /// <summary>Writes the registration the loopback daemon (pid 42, version 0.0.0-test) would have published.</summary>
    private static void Seed(string path, Uri endpoint)
    {
        var document = "{\"id\":\"isolated\",\"version\":\"" + ServiceInfoBodyData.Version + "\",\"url\":\"" + endpoint
            + "\",\"pid\":" + ServiceInfoBodyData.Pid.ToString(CultureInfo.InvariantCulture)
            + ",\"password\":\"" + IsolatedPassword + "\"}";
        _ = FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(path)!);
        FileSystem.File.WriteAllText(path, document);
    }
}
