using System.IO.Abstractions;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// One test host's turn at background-service elections: exclusive across the machine's hosts,
/// shared inside this one. The election classes run in each host's keyless serial tail, and the
/// hosts of one run start together and last about as long, so their tails — and the twenty-odd
/// source-run servers each election starts — used to land on the machine at the same moment
/// (<see cref="MachineLock.ServiceElection"/>). Each election class holds a turn from its first
/// test to its last, so the hosts take turns instead. Inside one host the count keeps a second
/// holder from waiting on its own host: the classes run serially already, and a class's
/// After hook may run after the next class's Before hook.
/// </summary>
internal static class ServiceElectionTurn
{
    /// <summary>
    /// How long a host waits for another host's turn to end: the other host's election classes run
    /// in two to four minutes, and a turn held past this bound fails the waiting class loudly.
    /// </summary>
    public const int WaitMilliseconds = 15 * 60 * 1000;

    /// <summary>The Before(Class) hook's own bound: the wait above plus the time to take the lock.</summary>
    public const int HookTimeoutMilliseconds = WaitMilliseconds + 60_000;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static MachineLock? _held;
    private static int _holders;

    /// <summary>Takes this host's turn, or joins it when a class of this host already holds it.</summary>
    public static async Task EnterAsync(IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_holders is 0)
            {
                _held = await MachineLock.AcquireAsync(
                    fileSystem, MachineLock.ServiceElection, TimeSpan.FromMilliseconds(WaitMilliseconds));
            }

            _holders++;
        }
        finally
        {
            _ = Gate.Release();
        }
    }

    /// <summary>Leaves the turn; the last holder of this host hands it to the next host.</summary>
    public static async Task LeaveAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_holders is 0)
            {
                // The Before hook failed before it held a turn; its After hook still runs.
                return;
            }

            _holders--;
            if (_holders is 0)
            {
                _held?.Dispose();
                _held = null;
            }
        }
        finally
        {
            _ = Gate.Release();
        }
    }
}
