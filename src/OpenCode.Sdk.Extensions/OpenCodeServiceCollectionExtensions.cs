using OpenCode.Sdk;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the opencode client family with a service collection: the root
/// <see cref="OpenCodeClient"/> as a singleton the container disposes, plus every
/// sub-client resolved from it so a consumer can inject <see cref="SessionsClient"/>
/// directly.
/// </summary>
public static class OpenCodeServiceCollectionExtensions
{
    /// <summary>Registers a client that owns its connection to the endpoint.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="endpoint">The absolute HTTP or HTTPS server endpoint.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddOpenCode(this IServiceCollection services, Uri endpoint) =>
        services.AddOpenCode(endpoint, configure: null);

    /// <summary>Registers a client that owns its connection to the endpoint.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="endpoint">The absolute HTTP or HTTPS server endpoint.</param>
    /// <param name="configure">Shapes the client options; the endpoint must stay unset on this path.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddOpenCode(this IServiceCollection services, Uri endpoint,
        Action<OpenCodeClientOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpoint);

        _ = services.AddSingleton(_ => new OpenCodeClient(endpoint, CreateOptions(configure)));
        return AddSubClients(services);
    }

    /// <summary>Registers a client over a caller-owned HttpClient; neither the SDK nor the container disposes it.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="httpClient">The caller-owned HTTP client.</param>
    /// <param name="configure">Shapes the client options; the endpoint is required on this path.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddOpenCode(this IServiceCollection services, HttpClient httpClient,
        Action<OpenCodeClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configure);

        _ = services.AddSingleton(_ => new OpenCodeClient(httpClient, CreateOptions(configure)));
        return AddSubClients(services);
    }

    private static OpenCodeClientOptions CreateOptions(Action<OpenCodeClientOptions>? configure)
    {
        var options = new OpenCodeClientOptions();
        configure?.Invoke(options);
        return options;
    }

    private static IServiceCollection AddSubClients(IServiceCollection services)
    {
        _ = services.AddSingleton(static provider => provider.GetRequiredService<OpenCodeClient>().Sessions);
        return services;
    }
}
