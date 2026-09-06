using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class SessionMoveEvents(string sessionId, string destination, OwnedEventReader reader)
{
    private readonly EventDiagnosticSummary _observed = new();

    public string DiagnosticSummary => _observed.ToString();

    public async Task<SessionMoved> CompleteAsync(
        IAsyncEnumerable<IEvent> events,
        TaskCompletionSource<bool> connected)
    {
        try
        {
            return await reader.Start(_ => ObserveAsync(events, connected));
        }
        catch (Exception exception)
        {
            exception.Data[EventDiagnosticSummary.DataKey] = DiagnosticSummary;
            throw;
        }
    }

    public async Task<SessionMoved> ObserveAsync(
        IAsyncEnumerable<IEvent> events,
        TaskCompletionSource<bool> connected)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(connected);
        try
        {
            await foreach (var @event in events.WithCancellation(reader.Token))
            {
                _observed.Add(@event.Type);
                if (@event is EventServerConnected)
                {
                    _ = connected.TrySetResult(true);
                }

                if (@event is SessionMoved moved
                    && moved.Data.SessionId == sessionId
                    && moved.Data.Location.Directory == destination)
                {
                    return moved;
                }
            }
        }
        catch (OperationCanceledException exception)
            when (reader.ObservationTimedOut(exception))
        {
            throw new TimeoutException(
                $"The session move did not complete within the event window. Events: {_observed}.",
                exception);
        }
        catch (Exception exception)
        {
            exception.Data[EventDiagnosticSummary.DataKey] = DiagnosticSummary;
            throw;
        }

        throw new InvalidOperationException(
            $"The event subscription ended before the session move completed. Events: {_observed}.");
    }
}
