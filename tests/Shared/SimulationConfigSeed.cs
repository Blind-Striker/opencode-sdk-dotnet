using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// The test-owned typed configuration shared by every simulated server consumer. The provider
/// follows config.ts <c>providers</c> and config/provider.ts, resolves the builtin
/// openai-compatible package without an install, and pins model traffic to the exact
/// <c>https://api.openai.com/v1/chat/completions</c> route claimed by the Drive network. The
/// permission agent gives live permission tests one explicit ask rule without changing ordinary
/// session defaults.
/// </summary>
internal sealed partial class SimulationConfigSeed
{
    internal const string CommandDescription = "The deterministic SDK live command.";
    internal const string CommandName = "sdk-live-command";
    internal const string CommandTemplate = "Report the live catalog for $ARGUMENTS";
    internal const string ModelId = "sim-model";
    internal const string ModelName = "Simulated Model";
    internal const string PermissionProbeAction = "sdk.live.permission";
    internal const string PermissionProbeAgentId = "permission-probe";
    internal const string ProviderId = "sim";
    internal const string ProviderName = "Simulated";
    internal const string ProviderPackage = "@opencode-ai/ai/providers/openai-compatible";
    internal const string ReferenceDescription = "The deterministic SDK live reference.";
    internal const string ReferenceName = "sdk-live-reference";
    internal const string ReferencePath = ".";

    internal static string Json { get; } = JsonSerializer.Serialize(
        new Configuration
        {
            Agents = new Dictionary<string, AgentConfiguration>(StringComparer.Ordinal)
            {
                [PermissionProbeAgentId] = new AgentConfiguration
                {
                    Permissions =
                    [
                        new PermissionRule
                        {
                            Action = PermissionProbeAction,
                            Resource = "*",
                            Effect = PermissionEffect.Ask,
                        },
                    ],
                },
            },
            Commands = new Dictionary<string, CommandConfiguration>(StringComparer.Ordinal)
            {
                [CommandName] = new CommandConfiguration
                {
                    Template = CommandTemplate,
                    Description = CommandDescription,
                },
            },
            Model = new ModelSelectionConfiguration
            {
                ProviderIdentifier = ProviderId,
                ModelIdentifier = ModelId,
            },
            Providers = new Dictionary<string, ProviderConfiguration>(StringComparer.Ordinal)
            {
                [ProviderId] = new ProviderConfiguration
                {
                    Name = ProviderName,
                    Package = ProviderPackage,
                    Settings = new ProviderSettings
                    {
                        BaseUrl = "https://api.openai.com/v1",
                        ApiKey = "drive-lease",
                    },
                    Models = new Dictionary<string, ModelConfiguration>(StringComparer.Ordinal)
                    {
                        [ModelId] = new ModelConfiguration { Name = ModelName },
                    },
                },
            },
            References = new Dictionary<string, ReferenceLocalConfiguration>(StringComparer.Ordinal)
            {
                [ReferenceName] = new ReferenceLocalConfiguration
                {
                    Path = ReferencePath,
                    Description = ReferenceDescription,
                },
            },
        },
        SerializationContext.Default.Configuration);

    private sealed record Configuration
    {
        public required IReadOnlyDictionary<string, AgentConfiguration> Agents { get; init; }

        public required IReadOnlyDictionary<string, CommandConfiguration> Commands { get; init; }

        public required ModelSelectionConfiguration Model { get; init; }

        public required IReadOnlyDictionary<string, ProviderConfiguration> Providers { get; init; }

        public required IReadOnlyDictionary<string, ReferenceLocalConfiguration> References { get; init; }
    }

    private sealed record AgentConfiguration
    {
        public required IReadOnlyList<PermissionRule> Permissions { get; init; }
    }

    private sealed record CommandConfiguration
    {
        public required string Template { get; init; }

        public required string Description { get; init; }
    }

    private sealed record ModelSelectionConfiguration
    {
        [JsonPropertyName("providerID")]
        public required string ProviderIdentifier { get; init; }

        [JsonPropertyName("model")]
        public required string ModelIdentifier { get; init; }
    }

    private sealed record ProviderConfiguration
    {
        public required string Name { get; init; }

        public required string Package { get; init; }

        public required ProviderSettings Settings { get; init; }

        public required IReadOnlyDictionary<string, ModelConfiguration> Models { get; init; }
    }

    private sealed record ProviderSettings
    {
        [JsonPropertyName("baseURL")]
        public required string BaseUrl { get; init; }

        public required string ApiKey { get; init; }
    }

    private sealed record ModelConfiguration
    {
        public required string Name { get; init; }
    }

    private sealed record ReferenceLocalConfiguration
    {
        public required string Path { get; init; }

        public required string Description { get; init; }
    }

    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Configuration))]
    private sealed partial class SerializationContext : JsonSerializerContext;
}
