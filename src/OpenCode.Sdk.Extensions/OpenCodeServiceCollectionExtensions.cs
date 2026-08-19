using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenCode.Sdk;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the opencode client family as singletons: one <see cref="OpenCodeClient"/>
/// owning its transport for the container's lifetime, plus every sub-client resolved from
/// that same instance so a consumer can inject <see cref="SessionsClient"/> directly.
/// </summary>
public static class OpenCodeServiceCollectionExtensions
{
    /// <summary>Registers the opencode client, shaping its options in code.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Shapes the client options; the endpoint is required.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenCode(this IServiceCollection services, Action<OpenCodeClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        _ = services.Configure(configure);
        return AddOpenCodeCore(services);
    }

    /// <summary>Registers the opencode client, binding its options from configuration.</summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">The configuration section carrying the client options.</param>
    /// <returns>The service collection for chaining.</returns>
    [RequiresDynamicCode("Configuration binding uses reflection over the options type; prefer the configure-action overload on native AOT.")]
    [RequiresUnreferencedCode("Configuration binding may require members trimming removes; prefer the configure-action overload when trimming.")]
    public static IServiceCollection AddOpenCode(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddOptions<OpenCodeClientOptions>().Bind(configuration);
        return AddOpenCodeCore(services);
    }

    private static IServiceCollection AddOpenCodeCore(IServiceCollection services)
    {
        // One singleton client owns the transport for the container's lifetime (pooled
        // connection lifetime keeps it healthy on modern TFMs); sub-clients resolve from
        // that same instance, so every injection shares one pipeline and the container
        // disposes one client at shutdown.
        _ = services.AddSingleton(static provider => new OpenCodeClient(provider.GetRequiredService<IOptions<OpenCodeClientOptions>>().Value));
        _ = services.AddSingleton(static EventsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Events);
        _ = services.AddSingleton(static SessionsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Sessions);
        _ = services.AddSingleton(static ShellsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Shells);
        return services;
    }
}
