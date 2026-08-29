using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// The pipeline's terminal stand-in: ends the policy chain without sending, so a benchmark can
/// drive one policy in isolation over a message that is never dispatched.
/// </summary>
internal sealed class NoOpTerminalPolicy : PipelinePolicy
{
    private NoOpTerminalPolicy()
    {
    }

    public static NoOpTerminalPolicy Instance { get; } = new();

    public override ValueTask ProcessAsync(PipelineMessage message, ReadOnlyMemory<PipelinePolicy> remaining) => ValueTask.CompletedTask;
}
