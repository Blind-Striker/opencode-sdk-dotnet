using OpenCode.Sdk.TestSupport;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class OwnedDriveInvocation
{
    private readonly DriveController _controller;

    public OwnedDriveInvocation(DriveController controller, DriveInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(invocation);
        _controller = controller;
        Invocation = invocation;
    }

    public DriveInvocation Invocation { get; }

    public bool IsFinished { get; private set; }

    public Task ChunkTextAsync(string text) =>
        _controller.ChunkTextAsync(Invocation.Id, text);

    public async Task FinishAsync()
    {
        await _controller.FinishAsync(Invocation.Id);
        IsFinished = true;
    }

    public async Task DisconnectAsync(CancellationToken _)
    {
        if (IsFinished)
        {
            return;
        }

        await _controller.DisconnectAsync(Invocation.Id);
        IsFinished = true;
    }
}
