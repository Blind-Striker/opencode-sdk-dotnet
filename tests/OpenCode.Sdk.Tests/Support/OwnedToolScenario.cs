using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.TestSupport;
using OpenCode.Sdk.TestSupport.Ownership;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// One owned model-tool scenario over the shared simulated server: an isolated workspace, a
/// client targeting it, the single event reader (<see cref="Probe"/>) attached before anything
/// else happens, the repository-owned <c>drive_echo</c> registration, and one session under the
/// permission-gated agent. Every controller wait is bounded and recorded here so cleanup knows
/// what is still outstanding; cleanup interrupts an unfinished session, requires the exact
/// <c>tool.cancel</c> for a tool it still holds, waits the session idle, resets the registration,
/// removes the session, and settles the reader, each under its own budget, preserving the
/// primary failure and every cleanup failure.
/// </summary>
internal sealed class OwnedToolScenario
{
    internal const string ToolName = "drive_echo";
    internal const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";
    internal const string DriveCallId = "call_drive";
    internal const string ReadCallId = "call_read";

    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RequestWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CancellationWait = TimeSpan.FromSeconds(30);

    private readonly SimulatedDriveServerFixture _server;
    private readonly TestWorkspace _workspace;
    private readonly OpenCodeClient _client;
    private readonly OwnedEventReader _reader;
    private readonly OwnedCleanup _cleanup = new(CleanupTimeout);
    private string? _sessionId;
    private string? _pendingToolId;
    private bool _registered;
    private bool _terminal;

    private OwnedToolScenario(SimulatedDriveServerFixture server, CancellationToken cancellationToken)
    {
        _server = server;
        _workspace = server.CreateWorkspace();
        _client = server.CreateClient(new LocationSelector { Directory = _workspace.Path });
        _reader = new OwnedEventReader(EventWait, CleanupTimeout, cancellationToken);
        Probe = new SessionEventProbe(_reader);
        _cleanup.Own("owned session interrupt", InterruptIfUnfinishedAsync);
        _cleanup.Own("owned tool cancellation", ObservePendingToolCancellationAsync);
        _cleanup.Own("owned session wait", WaitIdleAsync);
        _cleanup.Own("owned tool registration reset", ResetRegistrationAsync);
        _cleanup.Own("owned session removal", RemoveSessionAsync);
        _cleanup.Own("event reader", _ => _reader.CompleteAsync(null));
        _cleanup.Own("owned workspace", _ =>
        {
            _workspace.Dispose();
            return Task.CompletedTask;
        });
    }

    public SessionEventProbe Probe { get; }

    public OpenCodeClient Client => _client;

    public string SessionId => _sessionId ?? throw new InvalidOperationException("The scenario has no session yet.");

    public SessionClient Session => _client.Sessions.GetSessionClient(SessionId);

    /// <summary>The pending simulated-tool invocation this scenario still holds, if any.</summary>
    public string? PendingToolId => _pendingToolId;

    public static async Task<OwnedToolScenario> CreateAsync(
        SimulatedDriveServerFixture server,
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var scenario = new OwnedToolScenario(server, cancellationToken);
        Exception? failure = null;
        try
        {
            await scenario.InitializeAsync(title, cancellationToken);
            return scenario;
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (failure is not null)
            {
                await scenario.CompleteAsync(failure);
            }
        }
    }

    public async Task<DriveInvocation> WaitForModelRequestAsync()
    {
        var invocation = await _server.Controller.WaitForRequestAsync(RequestWait);
        if (invocation.Url != ChatCompletionsUrl || invocation.Model != SimulationConfigSeed.ModelId)
        {
            throw new InvalidOperationException(
                $"Expected model '{SimulationConfigSeed.ModelId}' at '{ChatCompletionsUrl}', but received model " +
                $"'{invocation.Model}' at '{invocation.Url}'.");
        }

        return invocation;
    }

    /// <summary>Waits for the owned tool's invocation and holds it until settled or cancelled.</summary>
    public async Task<DriveToolInvocation> WaitForToolInvocationAsync()
    {
        var invocation = await _server.Controller.WaitForToolInvocationAsync(RequestWait);
        _pendingToolId = invocation.Id;
        return invocation;
    }

    /// <summary>Records that the held tool invocation was settled (finished, failed, or observed cancelled).</summary>
    public void ReleasePendingTool() => _pendingToolId = null;

    public Task<DriveToolCancellation> WaitForToolCancellationAsync() =>
        _server.Controller.WaitForToolCancellationAsync(CancellationWait);

    /// <summary>Records that the session reached a terminal execution event, so cleanup does not interrupt it.</summary>
    public void MarkTerminal() => _terminal = true;

    /// <summary>Scripts the canonical single tool call for the current model request and ends the turn with tool-calls.</summary>
    public async Task ScriptToolCallAsync(DriveInvocation invocation, string callId, string name, JsonObject input)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await _server.Controller.ChunkToolCallAsync(invocation.Id, callId, name, DriveJson.Element(input));
        await _server.Controller.FinishAsync(invocation.Id, "tool-calls");
    }

    /// <summary>Scripts the final text of the current model request and ends the turn with stop.</summary>
    public async Task ScriptFinalTextAsync(DriveInvocation invocation, string text)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await _server.Controller.ChunkTextAsync(invocation.Id, text);
        await _server.Controller.FinishAsync(invocation.Id);
    }

    public Task UpdateToolAsync(DriveToolInvocation invocation, int sequence, JsonObject update)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return _server.Controller.UpdateToolAsync(invocation.Id, sequence, DriveJson.Element(update));
    }

    public async Task FinishToolAsync(DriveToolInvocation invocation, JsonObject structured, string text)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await _server.Controller.FinishToolAsync(invocation.Id, DriveJson.Element(structured), text);
        ReleasePendingTool();
    }

    public async Task FailToolAsync(DriveToolInvocation invocation, string message)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await _server.Controller.FailToolAsync(invocation.Id, message);
        ReleasePendingTool();
    }

    /// <summary>
    /// The real tool-result message the model loop resumed with, as the provider request carried
    /// it: the OpenAI chat lowering emits <c>{role:"tool", tool_call_id, content}</c>.
    /// </summary>
    public static string RequireToolResult(DriveInvocation invocation, string callId)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (invocation.Body.ValueKind is JsonValueKind.Object &&
            invocation.Body.TryGetProperty("messages", out var messages) &&
            messages.ValueKind is JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("role", out var role) && role.ValueKind is JsonValueKind.String &&
                    string.Equals(role.GetString(), "tool", StringComparison.Ordinal) &&
                    message.TryGetProperty("tool_call_id", out var id) &&
                    string.Equals(id.GetString(), callId, StringComparison.Ordinal))
                {
                    return message.TryGetProperty("content", out var content) && content.ValueKind is JsonValueKind.String
                        ? content.GetString()!
                        : throw new InvalidOperationException($"The tool result for '{callId}' carries no string content.");
                }
            }
        }

        throw new InvalidOperationException(
            $"The model request '{invocation.Id}' carries no tool result for '{callId}'. Body: {invocation.Body}");
    }

    public Task CompleteAsync(Exception? primaryFailure) => _cleanup.CompleteAsync(primaryFailure);

    private async Task InitializeAsync(string title, CancellationToken cancellationToken)
    {
        // The subscription is attached before anything is registered or created, so every
        // later event is observed by construction rather than by racing the subscription.
        Probe.Start(_client.Events.SubscribeAsync(_reader.Token));
        await Probe.WaitForConnectedAsync(cancellationToken);

        var registration = DriveToolRegistration.Parse(new FixtureLoader().LoadText("Simulation.drive-echo-tool.json"));
        await _server.Controller.AttachToolsAsync([registration]);
        _registered = true;

        var created = await _client.Sessions.CreateSessionAsync(
            new SessionCreateRequest
            {
                Title = title,
                Agent = SimulationConfigSeed.ReadAskAgentId,
                Model = new ModelRef { Id = SimulationConfigSeed.ModelId, ProviderId = SimulationConfigSeed.ProviderId },
                Location = new LocationRef { Directory = _workspace.Path },
            },
            cancellationToken: cancellationToken);
        _sessionId = created.Session.Id;
    }

    private async Task InterruptIfUnfinishedAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is null || _terminal)
        {
            return;
        }

        // Idle sessions answer Interrupted=false; cleanup accepts either, it only requires the call to succeed.
        var response = await Session.PostInterruptAsync(requestOptions: OpenCodeRequestOptions.NoThrow, cancellationToken: cancellationToken);
        if (response.IsError)
        {
            throw new InvalidOperationException(
                "Owned session interrupt returned status " + response.Status.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private async Task ObservePendingToolCancellationAsync(CancellationToken cancellationToken)
    {
        if (_pendingToolId is not { } pending)
        {
            return;
        }

        // The interruption above must reach the Drive tool: only the exact cancellation proves it,
        // and a tool that is neither settled nor cancelled is a leak this cleanup refuses to hide.
        var cancellation = await _server.Controller.WaitForToolCancellationAsync(CancellationWait).WaitAsync(cancellationToken);
        if (cancellation.Id != pending)
        {
            throw new InvalidOperationException(
                $"Expected the cancellation of tool invocation '{pending}' but observed '{cancellation.Id}'.");
        }

        _pendingToolId = null;
    }

    private async Task WaitIdleAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is null)
        {
            return;
        }

        var response = await Session.PostWaitAsync(OpenCodeRequestOptions.NoThrow, cancellationToken);
        if (response.Status != 204 || response.IsError)
        {
            throw new InvalidOperationException(
                "Owned session wait returned status " + response.Status.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private async Task ResetRegistrationAsync(CancellationToken cancellationToken)
    {
        if (!_registered)
        {
            return;
        }

        await _server.Controller.AttachToolsAsync([]).WaitAsync(cancellationToken);
        _registered = false;
    }

    private async Task RemoveSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is null)
        {
            return;
        }

        var response = await Session.RemoveSessionAsync(OpenCodeRequestOptions.NoThrow, cancellationToken);
        if (response.Status != 204 || response.IsError)
        {
            throw new InvalidOperationException(
                "Owned session removal returned status " + response.Status.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}
