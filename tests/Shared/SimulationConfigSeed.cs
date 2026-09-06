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
    internal const string PermissionProbeAction = "sdk.live.permission";
    internal const string PermissionProbeAgentId = "permission-probe";

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
            Providers = new Dictionary<string, ProviderConfiguration>(StringComparer.Ordinal)
            {
                ["sim"] = new ProviderConfiguration
                {
                    Name = "Simulated",
                    Package = "@opencode-ai/ai/providers/openai-compatible",
                    Settings = new ProviderSettings
                    {
                        BaseUrl = "https://api.openai.com/v1",
                        ApiKey = "drive-lease",
                    },
                    Models = new Dictionary<string, ModelConfiguration>(StringComparer.Ordinal)
                    {
                        ["sim-model"] = new ModelConfiguration { Name = "Simulated Model" },
                    },
                },
            },
        },
        SerializationContext.Default.Configuration);

    private sealed record Configuration
    {
        public required IReadOnlyDictionary<string, AgentConfiguration> Agents { get; init; }

        public required IReadOnlyDictionary<string, ProviderConfiguration> Providers { get; init; }
    }

    private sealed record AgentConfiguration
    {
        public required IReadOnlyList<PermissionRule> Permissions { get; init; }
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

    [JsonSourceGenerationOptions(
        GenerationMode = JsonSourceGenerationMode.Metadata,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Configuration))]
    private sealed partial class SerializationContext : JsonSerializerContext;
}
