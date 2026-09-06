namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedCleanupOperation
{
    private readonly Func<CancellationToken, Task>? _operation;
    private readonly Task? _pending;

    public OwnedCleanupOperation(string name, Func<CancellationToken, Task> operation)
    {
        Name = name;
        _operation = operation;
    }

    public OwnedCleanupOperation(string name, Task pending, Func<OperationCanceledException, bool>? expectedCancellation)
    {
        Name = name;
        _pending = pending;
        ExpectedCancellation = expectedCancellation;
    }

    public string Name { get; }

    public Func<OperationCanceledException, bool>? ExpectedCancellation { get; }

    public Task StartAsync(CancellationToken cancellationToken) => _pending ?? _operation!(cancellationToken);
}
