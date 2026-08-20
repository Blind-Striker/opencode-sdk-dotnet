using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// An adapter that materializes nothing, so a pipeline measurement through it isolates request
/// construction, decoration, sending, buffering, and UTF-8 validation from JSON and model cost.
/// </summary>
internal sealed class NoOpResponseAdapter : ResponseAdapter<NoOpResponse>
{
    private NoOpResponseAdapter()
    {
    }

    public static NoOpResponseAdapter Instance { get; } = new();

    public override int SuccessStatusCode => 200;

    public override NoOpResponse AdaptSuccess(int status, ReadOnlySpan<byte> utf8Body) => new()
    {
        Status = status,
    };

    public override NoOpResponse Adapt(int status, string rawBody) => new()
    {
        Status = status,
    };
}
