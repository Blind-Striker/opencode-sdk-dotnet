using System.Text.Json;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// The probe's barriers and its three diagnostic contracts. The barriers are proved against a
/// scripted two-part stream; the diagnostics against a stream of a hundred oversized event labels,
/// where an expired barrier names what it waited for, a subscription that ends before its first
/// event says so, and a faulted reader is preserved and named. Every message stays bounded.
/// </summary>
public sealed class SessionEventProbeTests
{
    private const int MessageBound = 2000;

    [Test]
    public async Task WaitForAsync_Should_Match_A_Retained_Event_Then_One_Arriving_After_It()
    {
        var first = new Marker("marker-a");
        var second = new Marker("marker-b");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = NewReader();
        var probe = new SessionEventProbe(reader);
        probe.Start(ScriptAsync([first], release.Task, [second]));
        Task<Marker>? anchored = null;
        try
        {
            var retained = await probe.WaitForAsync<Marker>(
                marker => marker.Type == "marker-a", "the first marker", CancellationToken.None);
            await Assert.That(retained).IsSameReferenceAs(first);

            // Anchored to the first, so the retained match cannot settle it; only the later one can.
            anchored = probe.WaitForAsync<Marker>(_ => true, "a marker after the first", first, CancellationToken.None);
            await Assert.That(anchored.IsCompleted).IsFalse();

            _ = release.TrySetResult(true);
            await Assert.That(await anchored).IsSameReferenceAs(second);
        }
        finally
        {
            _ = release.TrySetResult(true);
            await Drain(anchored);
            await reader.CompleteAsync(null);
        }
    }

    [Test]
    public async Task WaitForAsync_Should_Name_The_Barrier_And_Bound_Its_Diagnostics()
    {
        var scenario = new EventDiagnosticScenario { Stall = true };
        var reader = NewReader();
        var probe = new SessionEventProbe(reader);
        probe.Start(scenario.EventsAsync());
        try
        {
            await scenario.Stalled.Task;

            using var barrier = new CancellationTokenSource();
            await barrier.CancelAsync();
            var thrown = await Assert.That(async () =>
            {
                _ = await probe.WaitForAsync<SessionMoved>(
                    _ => true, "session.moved for the owned session", barrier.Token);
            }).Throws<TimeoutException>();

            await Assert.That(thrown!.Message).Contains("session.moved for the owned session");
            await AssertBoundedTranscriptAsync(thrown.Message);
        }
        finally
        {
            _ = scenario.Pending?.TrySetResult(true);
            await reader.CompleteAsync(null);
        }
    }

    [Test]
    public async Task WaitForConnectedAsync_Should_Report_A_Subscription_That_Ended_First()
    {
        var scenario = new EventDiagnosticScenario();
        var reader = NewReader();
        var probe = new SessionEventProbe(reader);
        probe.Start(scenario.EventsAsync());
        try
        {
            var thrown = await Assert.That(() => probe.WaitForConnectedAsync(CancellationToken.None))
                .Throws<InvalidOperationException>();

            await Assert.That(thrown!.Message).Contains("Reader: the event subscription ended");
            await AssertBoundedTranscriptAsync(thrown.Message);
        }
        finally
        {
            await reader.CompleteAsync(null);
        }
    }

    [Test]
    public async Task WaitForConnectedAsync_Should_Preserve_A_Faulted_Reader_And_Name_It()
    {
        var failure = new InvalidOperationException("reader transport failed");
        var scenario = new EventDiagnosticScenario { Failure = failure };
        var reader = NewReader();
        var probe = new SessionEventProbe(reader);
        probe.Start(scenario.EventsAsync());

        var thrown = await Assert.That(() => probe.WaitForConnectedAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
        _ = await Assert.That(() => reader.CompleteAsync(thrown)).Throws<InvalidOperationException>();

        await Assert.That(thrown).IsSameReferenceAs(failure);
        await Assert.That(probe.DiagnosticSummary)
            .Contains("it faulted (InvalidOperationException: reader transport failed)");
        await AssertBoundedTranscriptAsync(probe.DiagnosticSummary);
    }

    private static OwnedEventReader NewReader() => new(
        TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), CancellationToken.None, new OperationDeadlineScenario().Deadline);

    /// <summary>Yields the first events, waits for the caller's release, then yields the rest.</summary>
    private static async IAsyncEnumerable<IEvent> ScriptAsync(IEvent[] first, Task release, IEvent[] rest)
    {
        foreach (var @event in first)
        {
            yield return @event;
        }

        await release;
        foreach (var @event in rest)
        {
            yield return @event;
        }
    }

    /// <summary>
    /// Settles a barrier that may still be pending when the body failed. Awaited through WhenAny so
    /// the barrier's own outcome is observed without replacing the body's failure.
    /// </summary>
    private static async Task Drain(Task? pending)
    {
        if (pending is not null)
        {
            _ = await Task.WhenAny(pending);
        }
    }

    /// <summary>The transcript keeps the first and last labels, drops the middle, and stays short.</summary>
    private static async Task AssertBoundedTranscriptAsync(string message)
    {
        await Assert.That(message.Length).IsLessThan(MessageBound);
        await Assert.That(message).Contains("84 events omitted");
        await Assert.That(message).Contains("event-0000-");
        await Assert.That(message).Contains("event-0099-");
        await Assert.That(message).DoesNotContain("event-0050-");
    }

    /// <summary>
    /// A stand-in event: the probe under test reads only the marker, so the members the live
    /// event union hoists are answered explicitly and stay out of the probe's own surface.
    /// </summary>
    private sealed record Marker(string Type) : IEvent
    {
        string? IEvent.Id => null;

        IReadOnlyDictionary<string, JsonElement>? IEvent.Metadata => null;
    }
}
