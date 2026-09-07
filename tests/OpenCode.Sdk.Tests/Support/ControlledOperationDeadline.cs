namespace OpenCode.Sdk.Tests.Support;

internal sealed class ControlledOperationDeadline
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _expired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _won = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _delivery = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Task> _operation = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    /// <summary>
    /// The actual operation the held deadline was asked to observe, available once entered, so a
    /// proof can wait for that operation's own completion before it expires the deadline.
    /// </summary>
    public Task<Task> Operation => _operation.Task;

    public Task Won => _won.Task;

    public TimeoutException Failure { get; } = new("The controlled deadline elapsed.");

    public void Expire() => _ = _expired.TrySetException(Failure);

    public void Deliver() => _ = _delivery.TrySetResult(true);

    public void Release()
    {
        Expire();
        Deliver();
        // Teardown can precede entry into a later stage; observe its unused failure signal too.
        _ = _expired.Task.Exception;
    }

    public async Task WaitAsync(Task operation)
    {
        _ = _operation.TrySetResult(operation);
        _ = _entered.TrySetResult(true);
        try
        {
            await _expired.Task;
        }
        catch (TimeoutException)
        {
            _ = _won.TrySetResult(true);
            await _delivery.Task;
            throw;
        }
    }
}
