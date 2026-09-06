using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class SimulatedSessionTurn(
    SimulatedDriveServerFixture server,
    OpenCodeClient client,
    SessionClient session,
    string sessionId)
{
    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";
    private const string SimulatedModelId = "sim-model";
    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RequestWait = TimeSpan.FromSeconds(60);

    public async Task<SessionExecutionSucceeded> CompleteAsync(
        string prompt,
        string reply,
        CancellationToken cancellationToken)
    {
        using var eventWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cleanupSignal = new CancellationTokenSource();
        eventWindow.CancelAfter(EventWait);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<string>();
        var eventTask = Task.Run(
            () => ObserveCompletionAsync(
                reply, connected, observed, eventWindow.Token, cancellationToken, cleanupSignal.Token),
            cancellationToken);

        try
        {
            var attached = await Task.WhenAny(connected.Task, eventTask);
            if (attached == eventTask)
            {
                _ = await eventTask;
                throw new InvalidOperationException("The event subscription ended before server.connected.");
            }

            _ = await session.PostPromptAsync(
                new SessionPromptPostRequest { Text = prompt },
                cancellationToken: cancellationToken);

            var invocation = await DriveAsync(
                () => server.Controller.WaitForRequestAsync(RequestWait),
                "waiting for the model request");
            RequireSimulatedInvocation(invocation);

            await DriveAsync(
                () => server.Controller.ChunkTextAsync(invocation.Id, reply),
                "scripting the reply");
            await DriveAsync(
                () => server.Controller.FinishAsync(invocation.Id),
                "finishing the model turn");

            return await eventTask;
        }
        finally
        {
            await cleanupSignal.CancelAsync();
            await eventWindow.CancelAsync();
            try
            {
                _ = await eventTask;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The operation failed before the event window could finish. The primary failure
                // is already propagating; record the reader's expected cleanup cancellation.
                Console.WriteLine(
                    "simulated-session-turn: event reader cancelled during failure cleanup: " +
                    exception.GetType().Name);
            }
        }
    }

    private async Task<SessionExecutionSucceeded> ObserveCompletionAsync(
        string reply,
        TaskCompletionSource<bool> connected,
        List<string> observed,
        CancellationToken eventToken,
        CancellationToken callerToken,
        CancellationToken cleanupToken)
    {
        var sawOwnedText = false;
        try
        {
            await foreach (var @event in client.Events.SubscribeAsync(eventToken))
            {
                observed.Add(@event.Type);
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
            when (!callerToken.IsCancellationRequested && !cleanupToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The scripted turn did not complete within the event window. Events: {Quote(observed)}.",
                exception);
        }

        throw new InvalidOperationException(
            $"The event subscription ended before the scripted turn completed. Events: {Quote(observed)}.");
    }

    private static void RequireSimulatedInvocation(DriveInvocation invocation)
    {
        if (invocation.Model != SimulatedModelId || invocation.Url != ChatCompletionsUrl)
        {
            throw new InvalidOperationException(
                $"Expected model '{SimulatedModelId}' at '{ChatCompletionsUrl}', but received model " +
                $"'{invocation.Model}' at '{invocation.Url}'.");
        }
    }

    private static async Task<T> DriveAsync<T>(Func<Task<T>> operation, string description)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"The drive controller was torn down while {description}.", exception);
        }
    }

    private static async Task DriveAsync(Func<Task> operation, string description)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"The drive controller was torn down while {description}.", exception);
        }
    }

    private static string Quote(IEnumerable<string> values) =>
        string.Join(", ", values.Select(value => $"'{value}'"));
}
