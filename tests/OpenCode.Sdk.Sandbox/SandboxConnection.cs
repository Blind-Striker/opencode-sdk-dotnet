namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// How the sandbox reaches a server. By default it calls <see cref="OpenCodeServer.EnsureAsync"/>,
/// which is what the opencode CLI does before it connects: reuse the registered background service,
/// or start one when none is usable. That needs no endpoint and no password, and the server uses
/// your own opencode configuration. <c>--endpoint</c> names a server you run yourself; its
/// password comes from <c>OPENCODE_PASSWORD</c>, the variable <c>opencode serve</c> reads.
/// </summary>
internal sealed class SandboxConnection : IAsyncDisposable
{
    private readonly OpenCodeServer? _service;

    private SandboxConnection(OpenCodeServer? service, Action<OpenCodeClientOptions> configure, string description)
    {
        _service = service;
        Configure = configure;
        Description = description;
    }

    /// <summary>Applies the endpoint and credential to a client's options.</summary>
    public Action<OpenCodeClientOptions> Configure { get; }

    /// <summary>One line that says which server the sandbox reached.</summary>
    public string Description { get; }

    public static async Task<SandboxConnection> OpenAsync(Uri? endpoint)
    {
        if (endpoint is not null)
        {
            var password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD");
            return new SandboxConnection(
                service: null,
                options =>
                {
                    options.Endpoint = endpoint;
                    options.Password = password;
                },
                $"endpoint {endpoint}");
        }

        var service = await OpenCodeServer.EnsureAsync().ConfigureAwait(false);
        return new SandboxConnection(
            service,
            options =>
            {
                options.Endpoint = service.Endpoint;
                options.Username = service.Username;
                options.Password = service.Password;
            },
            $"background service {service.Endpoint} (pid {service.ProcessId})");
    }

    /// <summary>Releases the handle. The background service is shared, so it keeps running.</summary>
    public ValueTask DisposeAsync() => _service?.DisposeAsync() ?? default;
}
