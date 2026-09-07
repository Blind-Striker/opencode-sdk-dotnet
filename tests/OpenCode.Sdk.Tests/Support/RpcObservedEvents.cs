using System.Text.Json;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Follows the live event bus until the owned rpc plugin's <c>observed</c> event carries the
/// caller's nonce. The connected frame is signalled first so the caller can emit only after the
/// subscription is attached; nothing is claimed about events that arrived before attachment.
/// </summary>
internal sealed class RpcObservedEvents(string eventType, string nonce, OwnedEventReader reader)
{
    private readonly EventDiagnosticSummary _observed = new();

    public string DiagnosticSummary => _observed.ToString();

    public async Task<EventRpc> CompleteAsync(
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

    private async Task<EventRpc> ObserveAsync(
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

                if (@event is EventRpc rpc
                    && string.Equals(rpc.Type, eventType, StringComparison.Ordinal)
                    && rpc.Data.TryGetValue("nonce", out var observedNonce)
                    && observedNonce.ValueKind == JsonValueKind.String
                    && string.Equals(observedNonce.GetString(), nonce, StringComparison.Ordinal))
                {
                    return rpc;
                }
            }
        }
        catch (OperationCanceledException exception)
            when (reader.ObservationTimedOut(exception))
        {
            throw new TimeoutException(
                $"The rpc event '{eventType}' with nonce '{nonce}' did not arrive within the event window. " +
                $"Events: {_observed}.",
                exception);
        }
        catch (Exception exception)
        {
            exception.Data[EventDiagnosticSummary.DataKey] = DiagnosticSummary;
            throw;
        }

        throw new InvalidOperationException(
            $"The event subscription ended before the rpc event '{eventType}' arrived. Events: {_observed}.");
    }
}
