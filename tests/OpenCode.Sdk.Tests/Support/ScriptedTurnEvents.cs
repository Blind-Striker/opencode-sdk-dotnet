using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class ScriptedTurnEvents(string reply, string sessionId, OwnedEventReader reader)
{
    private readonly EventDiagnosticSummary _observed = new();

    public string DiagnosticSummary => _observed.ToString();

    public async Task<SessionExecutionSucceeded> CompleteAsync(
        IAsyncEnumerable<IEvent> events, TaskCompletionSource<bool> connected)
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

    public async Task<SessionExecutionSucceeded> ObserveAsync(
        IAsyncEnumerable<IEvent> events, TaskCompletionSource<bool> connected)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(connected);
        var sawOwnedText = false;
        try
        {
            await foreach (var @event in events.WithCancellation(reader.Token))
            {
                _observed.Add(@event.Type);
                if (@event is EventServerConnected)
                {
                    _ = connected.TrySetResult(true);
                }

                if (@event is SessionTextEnded textEnded
                    && textEnded.Data.SessionId == sessionId
                    && textEnded.Data.Text == reply)
                {
                    sawOwnedText = true;
                    continue;
                }

                if (sawOwnedText
                    && @event is SessionExecutionSucceeded succeeded
                    && succeeded.Data.SessionId == sessionId)
                {
                    return succeeded;
                }
            }
        }
        catch (OperationCanceledException exception)
            when (reader.ObservationTimedOut(exception))
        {
            throw new TimeoutException(
                $"The scripted turn did not complete within the event window. Events: {_observed}.",
                exception);
        }
        catch (Exception exception)
        {
            exception.Data[EventDiagnosticSummary.DataKey] = DiagnosticSummary;
            throw;
        }

        throw new InvalidOperationException(
            $"The event subscription ended before the scripted turn completed. Events: {_observed}.");
    }

}
