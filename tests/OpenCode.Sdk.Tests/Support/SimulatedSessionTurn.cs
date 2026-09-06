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
        var reader = new OwnedEventReader(EventWait, TimeSpan.FromSeconds(15), cancellationToken);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ScriptedTurnEvents(reply, sessionId, reader);
        var eventTask = events.CompleteAsync(client.Events.SubscribeAsync(reader.Token), connected);
        Exception? primaryFailure = null;

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
        catch (Exception exception)
        {
            primaryFailure = exception;
            exception.Data[EventDiagnosticSummary.DataKey] = events.DiagnosticSummary;
            throw;
        }
        finally
        {
            await reader.CompleteAsync(primaryFailure);
        }
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

}
