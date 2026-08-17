using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class BindingScenarioData
{
    public static OperationSelection Selection(params string[] operationIds) =>
        new()
        {
            OperationIds = operationIds,
        };

    public static GenerationCuration Curation(
        IReadOnlyDictionary<string, GroupCuration> groups,
        IReadOnlyDictionary<string, string>? envelopePayloadNames = null,
        IReadOnlyList<SchemaAlias>? schemaAliases = null) =>
        new()
        {
            Groups = groups,
            EnvelopePayloadNames = envelopePayloadNames ?? new Dictionary<string, string>(StringComparer.Ordinal),
            SchemaAliases = schemaAliases ?? [],
        };

    public static SchemaAlias Alias(string schema, string aliasOf, string reason = "The upstream spec emits a duplicate component.") =>
        new()
        {
            Schema = schema,
            AliasOf = aliasOf,
            Reason = reason,
        };

    public static Dictionary<string, GroupCuration> Groups(string wireName, GroupCuration group) =>
        new(StringComparer.Ordinal)
        {
            [wireName] = group,
        };

    public static GroupCuration RootGroup() =>
        new()
        {
            Placement = GroupPlacement.Root,
        };

    public static GroupCuration ClientGroup(string clientName = "Sessions", string? handleName = "SessionClient",
        string? handleParameter = "sessionID") =>
        new()
        {
            Placement = GroupPlacement.Client,
            ClientName = clientName,
            HandleName = handleName,
            HandleParameter = handleParameter,
        };
}
