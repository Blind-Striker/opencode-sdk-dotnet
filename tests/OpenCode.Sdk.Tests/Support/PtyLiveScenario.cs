using System.Globalization;
using System.Runtime.ExceptionServices;
using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Owns the normal PTY live test's server-relative location and cleanup. External mode resolves
/// its directory from that server; owned mode creates a workspace under the fixture's run root.
/// </summary>
internal sealed class PtyLiveScenario
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly List<Task> _pendingOperations = [];
    private readonly List<PtySession> _sessions = [];
    private readonly PinnedOpenCodeServerFixture _server;
    private OpenCodeClient? _client;
    private string? _directory;
    private LocationSelector? _location;
    private string? _mode;
    private TestWorkspace? _workspace;
    private PtyClient? _terminal;
    private bool _removed;
    private int _cleaned;

    public PtyLiveScenario(PinnedOpenCodeServerFixture server)
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
    }

    public OpenCodeClient Client =>
        _client ?? throw new InvalidOperationException("The PTY live scenario has not initialized.");

    public string Directory =>
        _directory ?? throw new InvalidOperationException("The PTY live scenario has not initialized.");

    public LocationSelector Location =>
        _location ?? throw new InvalidOperationException("The PTY live scenario has not initialized.");

    public string Mode =>
        _mode ?? throw new InvalidOperationException("The PTY live scenario has not initialized.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("The PTY live scenario has already initialized.");
        }

        if (_server.IsExternal)
        {
            _client = _server.CreateClient();
            var response = await _client.GetLocationAsync(cancellationToken: cancellationToken);
            _directory = response.ResolvedLocation.Directory;
            if (string.IsNullOrWhiteSpace(_directory))
            {
                throw new InvalidOperationException("The external server returned a blank location directory.");
            }

            _location = new LocationSelector { Directory = _directory };
            _mode = "external";
            return;
        }

        _workspace = _server.CreateWorkspace();
        _directory = _workspace.Path;
        _location = new LocationSelector { Directory = _directory };
        _client = _server.CreateClient(_location);
        _mode = "owned";
    }

    public PtySession Own(PtySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions.Add(session);
        return session;
    }

    public void Own(PtyClient terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        _terminal = terminal;
    }

    public void Observe(Task operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _pendingOperations.Add(operation);
    }

    public void MarkRemoved() => _removed = true;

    public async Task CleanupAsync(Exception? primaryFailure)
    {
        if (Interlocked.Exchange(ref _cleaned, 1) is 1)
        {
            ExceptionDispatchInfo.Capture(
                primaryFailure ?? new InvalidOperationException("The PTY live scenario was cleaned twice.")).Throw();
        }

        var failures = new List<Exception>();
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        await RemoveTerminalAsync(failures, cleanup.Token);
        await ObservePendingOperationsAsync(failures, cleanup.Token);
        await DisposeSessionsAsync(failures);
        DisposeOwners(failures);

        if (primaryFailure is not null)
        {
            if (failures.Count > 0)
            {
                primaryFailure.Data["PtyLiveScenario.CleanupFailures"] = new AggregateException(failures);
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (failures.Count is 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Multiple failures occurred while cleaning the PTY live scenario.", failures);
        }
    }

    private async Task RemoveTerminalAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        if (_removed || _terminal is null)
        {
            return;
        }

        try
        {
            var removed = await _terminal.RemovePtyAsync(
                new PtyRemoveRequest { Location = Location },
                OpenCodeRequestOptions.NoThrow,
                cancellationToken);
            if (removed.Status is not 204 and not 404)
            {
                failures.Add(new InvalidOperationException(
                    "PTY cleanup answered HTTP " + removed.Status.ToString(CultureInfo.InvariantCulture) + "."));
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private async Task ObservePendingOperationsAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        foreach (var operation in _pendingOperations)
        {
            try
            {
                await operation.WaitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private async Task DisposeSessionsAsync(List<Exception> failures)
    {
        foreach (var session in _sessions)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private void DisposeOwners(List<Exception> failures)
    {
        try
        {
            _client?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _workspace?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
