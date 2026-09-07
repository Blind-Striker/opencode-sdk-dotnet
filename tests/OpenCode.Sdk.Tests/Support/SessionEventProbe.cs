using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// One reader over the live event bus for a whole scenario: every event is retained in arrival
/// order for subsequence assertions, the connected frame is signalled so a caller can act only
/// once the subscription is attached, and typed barriers wait for the first event matching a
/// predicate, already retained or still to come. The loop runs until the owning reader ends it.
/// </summary>
internal sealed class SessionEventProbe
{
    private readonly OwnedEventReader _reader;
    private readonly EventDiagnosticSummary _summary = new();
    private readonly List<IEvent> _events = [];
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<bool> _connected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> _arrival = NewSignal();
    private Task? _loop;
    private string? _loopState;

    public SessionEventProbe(OwnedEventReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <summary>
    /// What arrived, and what became of the reader once it stopped. A barrier that expires while
    /// the loop was already torn down, or while the subscription had ended, reads as that rather
    /// than as an unexplained missing event.
    /// </summary>
    public string DiagnosticSummary
    {
        get
        {
            var reason = Volatile.Read(ref _loopState);
            return reason is null ? _summary.ToString() : _summary + ". Reader: " + reason;
        }
    }

    /// <summary>Starts the single reader; the caller awaits <see cref="WaitForConnectedAsync"/> before acting.</summary>
    public void Start(IAsyncEnumerable<IEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var loop = Task.Run(() => ObserveAsync(events), CancellationToken.None);
        _loop = loop;
        _reader.Own(loop);
    }

    /// <summary>
    /// Waits for the connected frame, racing the reader itself: a subscription that ends or faults
    /// before its first event must surface as its own failure, never as a later barrier expiry that
    /// hides the cause.
    /// </summary>
    public async Task WaitForConnectedAsync(CancellationToken cancellationToken)
    {
        var loop = _loop ?? throw new InvalidOperationException("The event probe has not started.");
        var attached = await Task.WhenAny(_connected.Task, loop).WaitAsync(cancellationToken);
        if (attached != loop)
        {
            return;
        }

        await loop;
        throw new InvalidOperationException(
            $"The event subscription ended before its first event. Events: {DiagnosticSummary}.");
    }

    /// <summary>The retained events so far, in arrival order.</summary>
    public IReadOnlyList<IEvent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }

    /// <summary>
    /// The first event of <typeparamref name="T"/> matching <paramref name="predicate"/>, retained
    /// already or arriving later; a wait that ends by cancellation reports what did arrive.
    /// </summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool> predicate, string description, CancellationToken cancellationToken)
        where T : IEvent
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var scanned = 0;
        while (true)
        {
            Task signal;
            lock (_gate)
            {
                for (; scanned < _events.Count; scanned++)
                {
                    if (_events[scanned] is T candidate && predicate(candidate))
                    {
                        return candidate;
                    }
                }

                signal = _arrival.Task;
            }

            try
            {
                await signal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                throw new TimeoutException(
                    $"No event satisfied '{description}' before the wait ended. Events: {DiagnosticSummary}.", exception);
            }
        }
    }

    private async Task ObserveAsync(IAsyncEnumerable<IEvent> events)
    {
        try
        {
            await foreach (var @event in events.WithCancellation(_reader.Token))
            {
                _summary.Add(@event.Type);
                if (@event is EventServerConnected)
                {
                    _ = _connected.TrySetResult(true);
                }

                TaskCompletionSource<bool> arrived;
                lock (_gate)
                {
                    _events.Add(@event);
                    arrived = _arrival;
                    _arrival = NewSignal();
                }

                _ = arrived.TrySetResult(true);
            }

            // The server closed the stream. Recorded rather than thrown: the owning reader
            // observes this task, and a barrier still waiting needs to read why nothing more
            // arrived instead of reporting an opaque expiry.
            Volatile.Write(ref _loopState, "the event subscription ended");
        }
        catch (OperationCanceledException exception) when (_reader.IsTeardownCancellation(exception))
        {
            // The owning reader ended the loop at teardown: the expected end of a probe, and the
            // reason a barrier racing that teardown must be able to name.
            Volatile.Write(ref _loopState, "the owning reader ended it at teardown");
        }
        catch (Exception exception)
        {
            // Rethrown for the owning reader to observe; recorded first so a barrier racing this
            // fault names the connection rather than reporting an unexplained missing event.
            Volatile.Write(ref _loopState, $"it faulted ({exception.GetType().Name}: {exception.Message})");
            throw;
        }
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
