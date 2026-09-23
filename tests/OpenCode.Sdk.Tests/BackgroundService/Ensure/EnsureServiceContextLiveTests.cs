using OpenCode.Sdk.Tests.Support;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests.BackgroundService.Ensure;

/// <summary>
/// The boundary the Ensure live tests stand on: an <see cref="EnsureServiceContext"/> leaves no
/// process behind. Proven against the pin's own source-run contenders, because the failure it
/// guards against — a losing contender that is still starting when the elected service is ended —
/// needs real contenders racing a real election.
/// </summary>
/// <remarks>
/// Keyless <c>[NotInParallel]</c>, like the Ensure live tests: the race only shows while the host
/// runs nothing else, and a loaded host would stretch the window this test watches.
/// </remarks>
[NotInParallel]
public sealed class EnsureServiceContextLiveTests
{
    private static readonly RealFileSystem FileSystem = new();

    /// <summary>How long each contender is given to leave after disposal: past a loaded host's contender start.</summary>
    private static readonly TimeSpan TakeoverWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Disposing the context right after a ten-caller election, while the losing contenders are
    /// still starting, leaves no process the election started. A loser still starting when the
    /// elected service ends finds nothing registered and takes the registration over, and a loser
    /// that did is elected and never leaves on its own; so every recorded contender is watched past
    /// the point one would have registered.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task DisposeAsync_Should_Leave_No_Process_The_Election_Started(CancellationToken cancellationToken)
    {
        var context = await EnsureServiceContext.CreateAsync(cancellationToken);
        IReadOnlyList<ProcessMark> started;
        try
        {
            var options = new OpenCodeServerEnsureOptions
            {
                RegistrationFilePath = context.RegistrationFile,
                Command = [context.ShimPath, "serve", "--service"],
                Environment = context.Environment,
            };
            _ = await Task.WhenAll(
                Enumerable.Range(0, 10).Select(_ => context.EnsureAsync(options, cancellationToken)));
            started = await context.ReadLiveContendersAsync(cancellationToken);
        }
        finally
        {
            await context.DisposeAsync();
        }

        var live = started.Where(mark => mark.IsRunning(FileSystem)).ToList();
        var exited = await Task.WhenAll(live.Select(mark =>
            ProcessObservation.ObserveExitWithinAsync(mark.ProcessId, TakeoverWindow, cancellationToken)));
        var survivors = live.Where((mark, index) => !exited[index] && mark.IsRunning(FileSystem)).ToList();
        try
        {
            await Assert.That(started).IsNotEmpty();
            await Assert.That(survivors).IsEmpty()
                .Because("a contender still starting at disposal must not outlive the context");
        }
        finally
        {
            foreach (var survivor in survivors)
            {
                ProcessObservation.KillIfRunning(survivor.ProcessId);
            }
        }
    }
}
