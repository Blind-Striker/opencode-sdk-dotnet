namespace OpenCode.Sdk.Extensions.Tests.Support;

/// <summary>Counts the requests flowing through a composed delegating-handler chain.</summary>
internal sealed class WitnessHandler : DelegatingHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return base.SendAsync(request, cancellationToken);
    }
}
