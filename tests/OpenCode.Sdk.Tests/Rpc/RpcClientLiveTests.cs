using System.Globalization;
using System.Text.Json;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The rpc family's live proof against the pinned server: <c>rpc.call</c> for an rpc id nobody
/// registered answers the declared 400 <see cref="RpcError"/> arm carrying the upstream reason
/// <c>rpc.unavailable</c>, on both error channels. Deterministic at the pin: only a plugin can
/// register an rpc (<c>ctx.rpc.register</c>), no builtin plugin does, and the handler awaits
/// plugin activation before it consults the registry, so the empty slot is never a race against
/// startup. The rpc id is checked before the method, so the method name is immaterial here.
/// </summary>
[ClassDataSource<PinnedOpenCodeServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(ParallelConstraintKeys.ServerProcess)]
public sealed class RpcClientLiveTests(PinnedOpenCodeServerFixture server)
{
    /// <summary>An rpc id no plugin at the pin registers; the server's message must name it back.</summary>
    private const string UnregisteredRpcId = "sdk-live-absent";

    private const string Method = "ping";

    /// <summary>The upstream reason for an empty registration slot (<c>core/src/rpc.ts</c>, <c>Rpc.call</c>).</summary>
    private const string UnavailableReason = "rpc.unavailable";

    private readonly FixtureLoader _fixtures = new();

    [Test]
    [Timeout(60_000)]
    public async Task PostCallAsync_Should_Answer_The_Unavailable_Arm_On_The_NoThrow_Spine_When_No_Rpc_Is_Registered(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var response = await client.Rpc.PostCallAsync(
            UnregisteredRpcId, Method, CallRequest(), OpenCodeRequestOptions.NoThrow, cancellationToken);

        await Assert.That(response.Status).IsEqualTo(400);
        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Error).IsTypeOf<RpcError>();
        var error = response.Error as RpcError;
        await Assert.That(error?.Type).IsEqualTo(UnavailableReason);
        await Assert.That(error?.Message).Contains(UnregisteredRpcId);
        // The handler spreads `data` only when the failure carries one, and the unavailable
        // failure carries none - so the member stays absent on the wire and null here.
        await Assert.That(error?.Data).IsNull();

        // Every value is what the server answered; the raw body is the evidence a reader can
        // compare against the upstream handler without trusting this test's typed view of it.
        Console.WriteLine(
            "rpc-live: arm=no-throw status=" + Number(response.Status) +
            " type=" + error?.Type +
            " message=" + error?.Message +
            " body=" + response.RawBody);
    }

    [Test]
    [Timeout(60_000)]
    public async Task PostCallAsync_Should_Throw_The_Unavailable_Arm_When_No_Rpc_Is_Registered(
        CancellationToken cancellationToken)
    {
        using var client = server.CreateClient();

        var exception = await Assert
            .That(async () => _ = await client.Rpc.PostCallAsync(
                UnregisteredRpcId, Method, CallRequest(), cancellationToken: cancellationToken))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<RpcError>();
        var error = exception.Error as RpcError;
        await Assert.That(error?.Type).IsEqualTo(UnavailableReason);
        await Assert.That(error?.Message).Contains(UnregisteredRpcId);

        Console.WriteLine(
            "rpc-live: arm=throw status=" + Number(exception.Status) +
            " type=" + error?.Type +
            " body=" + exception.RawBody);
    }

    /// <summary>
    /// The call body: an arbitrary JSON input the registry never gets to validate, because the
    /// rpc id lookup fails first. It is still a real body so the request the server refuses is
    /// the same shape a registered rpc would receive.
    /// </summary>
    private RpcCallPostRequest CallRequest()
    {
        using var document = JsonDocument.Parse(_fixtures.LoadJson("Rpc.rpc-call-input.json"));
        return new RpcCallPostRequest { Input = document.RootElement.Clone() };
    }

    /// <summary>Renders one number for the console line, culture-free.</summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
