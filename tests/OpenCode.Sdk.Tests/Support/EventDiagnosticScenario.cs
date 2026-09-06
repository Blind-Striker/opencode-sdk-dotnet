using System.Globalization;
using System.Runtime.CompilerServices;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class EventDiagnosticScenario : SessionClient
{
    public Exception? Failure { get; init; }

    public bool IncludeMarker { get; init; }

    public bool Stall { get; init; }

    public TaskCompletionSource<bool>? Pending { get; private set; }

    public override async IAsyncEnumerable<ISessionLogItem> GetLogAsync(
        SessionLogRequest? request = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (IncludeMarker)
        {
            yield return new EventLogSynced { AggregateId = "ses_owned", Seq = 1 };
        }

        foreach (var item in Observations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }

    public async IAsyncEnumerable<IEvent> EventsAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending = completion;
        foreach (var item in Observations())
        {
            yield return item;
        }

        if (Stall)
        {
            _ = await completion.Task;
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }

    private static IEnumerable<Observation> Observations() => Enumerable.Range(0, 100)
        .Select(index => new Observation("event-" + index.ToString("D4", CultureInfo.InvariantCulture)
                                        + "-" + new string('x', 4000)));

    private sealed record Observation(string Type) : ISessionLogItem, IEvent;
}
