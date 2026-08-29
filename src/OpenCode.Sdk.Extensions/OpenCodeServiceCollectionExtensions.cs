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
        _ = services.AddSingleton(static AgentsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Agents);
        _ = services.AddSingleton(static CommandsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Commands);
        _ = services.AddSingleton(static CredentialsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Credentials);
        _ = services.AddSingleton(static DebugClient (provider) => provider.GetRequiredService<OpenCodeClient>().Debug);
        _ = services.AddSingleton(static EventsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Events);
        _ = services.AddSingleton(static ExperimentalClient (provider) => provider.GetRequiredService<OpenCodeClient>().Experimental);
        _ = services.AddSingleton(static FormsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Forms);
        _ = services.AddSingleton(static GenerationClient (provider) => provider.GetRequiredService<OpenCodeClient>().Generation);
        _ = services.AddSingleton(static IntegrationsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Integrations);
        _ = services.AddSingleton(static LanguageModelsClient (provider) => provider.GetRequiredService<OpenCodeClient>().LanguageModels);
        _ = services.AddSingleton(static McpServersClient (provider) => provider.GetRequiredService<OpenCodeClient>().McpServers);
        _ = services.AddSingleton(static PermissionsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Permissions);
        _ = services.AddSingleton(static PersistentPtysClient (provider) => provider.GetRequiredService<OpenCodeClient>().PersistentPtys);
        _ = services.AddSingleton(static PluginsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Plugins);
        _ = services.AddSingleton(static ProjectsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Projects);
        _ = services.AddSingleton(static ProvidersClient (provider) => provider.GetRequiredService<OpenCodeClient>().Providers);
        _ = services.AddSingleton(static PtysClient (provider) => provider.GetRequiredService<OpenCodeClient>().Ptys);
        _ = services.AddSingleton(static ReferencesClient (provider) => provider.GetRequiredService<OpenCodeClient>().References);
        _ = services.AddSingleton(static ServerClient (provider) => provider.GetRequiredService<OpenCodeClient>().Server);
        _ = services.AddSingleton(static SessionsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Sessions);
        _ = services.AddSingleton(static ShellsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Shells);
        _ = services.AddSingleton(static SkillsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Skills);
        _ = services.AddSingleton(static VcsClient (provider) => provider.GetRequiredService<OpenCodeClient>().Vcs);
        _ = services.AddSingleton(static WebsearchClient (provider) => provider.GetRequiredService<OpenCodeClient>().Websearch);
        _ = services.AddSingleton(static WorkspacesClient (provider) => provider.GetRequiredService<OpenCodeClient>().Workspaces);
        _ = services.AddSingleton(static WorktreesClient (provider) => provider.GetRequiredService<OpenCodeClient>().Worktrees);
        return services;
    }
}
