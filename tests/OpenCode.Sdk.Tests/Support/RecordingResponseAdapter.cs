using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class RecordingResponseAdapter : ResponseAdapter<TestResponse>
{
    private readonly Func<int, string, TestResponse> _map;

    public RecordingResponseAdapter()
        : this(static (status, _) => new TestResponse
        {
            Status = status,
        })
    {
    }

    public RecordingResponseAdapter(Func<int, string, TestResponse> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        _map = map;
    }

    public int? AdaptedStatus { get; private set; }

    public string? AdaptedRawBody { get; private set; }

    public override TestResponse Adapt(int status, string rawBody)
    {
        AdaptedStatus = status;
        AdaptedRawBody = rawBody;
        return _map(status, rawBody);
    }
}
