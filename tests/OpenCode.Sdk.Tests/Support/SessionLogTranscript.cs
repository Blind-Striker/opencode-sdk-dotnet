using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class SessionLogTranscript(SessionClient session)
{
    private static readonly TimeSpan FollowWindow = TimeSpan.FromSeconds(120);
    private readonly List<ISessionLogItem> _liveItems = [];
    private readonly List<ISessionLogItem> _replayItems = [];
    private CancellationToken _callerToken;
    private IAsyncEnumerator<ISessionLogItem>? _enumerator;
    private CancellationTokenSource? _window;

    public IReadOnlyList<ISessionLogItem> LiveItems => _liveItems;

    public EventLogSynced? Marker { get; private set; }

    public IReadOnlyList<ISessionLogItem> ReplayItems => _replayItems;

    public SessionExecutionSucceeded? Succeeded { get; private set; }

    public SessionTextEnded? TextEnded { get; private set; }

    public async Task<List<ISessionLogItem>> ReadToEndAsync(
        SessionLogRequest request,
        CancellationToken cancellationToken)
    {
        var items = new List<ISessionLogItem>();
        await foreach (var item in session.GetLogAsync(request, cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    public async Task AttachAsync(string after, CancellationToken cancellationToken)
    {
        if (_enumerator is not null)
        {
            throw new InvalidOperationException("The session log transcript is already attached.");
        }

        _callerToken = cancellationToken;
        _window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _window.CancelAfter(FollowWindow);
        _enumerator = session.GetLogAsync(
                new SessionLogRequest { After = after, Follow = QueryBoolean.True },
                _window.Token)
            .GetAsyncEnumerator(_window.Token);
        while (await MoveNextAsync("the log.synced attachment marker"))
        {
            if (_enumerator.Current is EventLogSynced marker)
            {
                Marker = marker;
                return;
            }

            _replayItems.Add(_enumerator.Current);
        }

        throw EndedBefore("the log.synced attachment marker");
    }

    public async Task ReadTurnAsync(string sessionId, string reply)
    {
        if (Marker is null || _enumerator is null)
        {
            throw new InvalidOperationException("The session log transcript is not attached.");
        }

        var sawOwnedText = false;
        while (await MoveNextAsync("the followed turn completed"))
        {
            var item = _enumerator.Current;
            _liveItems.Add(item);
            if (item is SessionTextEnded text
                && text.Data.SessionId == sessionId
                && text.Data.Text == reply)
            {
                TextEnded = text;
                sawOwnedText = true;
                continue;
            }

            if (sawOwnedText
                && item is SessionExecutionSucceeded succeeded
                && succeeded.Data.SessionId == sessionId)
            {
                Succeeded = succeeded;
                return;
            }
        }

        throw EndedBefore("the followed turn completed");
    }

    public async Task DisposeAsync(CancellationToken cancellationToken)
    {
        var enumerator = Interlocked.Exchange(ref _enumerator, null);
        var window = Interlocked.Exchange(ref _window, null);
        if (window is null)
        {
            return;
        }

        await window.CancelAsync();
        try
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().AsTask().WaitAsync(cancellationToken);
            }
        }
        finally
        {
            window.Dispose();
        }
    }

    private async Task<bool> MoveNextAsync(string target)
    {
        var enumerator = _enumerator
                         ?? throw new InvalidOperationException("The session log transcript is not attached.");
        try
        {
            return await enumerator.MoveNextAsync();
        }
        catch (OperationCanceledException exception)
            when (!_callerToken.IsCancellationRequested && _window!.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The session log timed out before {target}. Items: {QuoteObservedItems()}.",
                exception);
        }
    }

    private InvalidOperationException EndedBefore(string target) =>
        new($"The session log ended before {target}. Items: {QuoteObservedItems()}.");

    private string QuoteObservedItems() =>
        string.Join(", ", _replayItems.Concat(_liveItems).Select(item => $"'{item.Type}'"));
}
