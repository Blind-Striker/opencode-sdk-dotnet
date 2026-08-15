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
        IReadOnlyList<PropertyOverride>? propertyOverrides = null,
        IReadOnlyList<SchemaAlias>? schemaAliases = null,
        IReadOnlyList<MutuallyExclusiveQuery>? mutuallyExclusiveQueries = null) =>
        new()
        {
            Groups = groups,
            EnvelopePayloadNames = envelopePayloadNames ?? new Dictionary<string, string>(StringComparer.Ordinal),
            PropertyOverrides = propertyOverrides ?? [],
            SchemaAliases = schemaAliases ?? [],
            MutuallyExclusiveQueries = mutuallyExclusiveQueries ?? [],
        };

    public static MutuallyExclusiveQuery ExclusiveQuery(string operation, string first, string second) =>
        new()
        {
            Operation = operation,
            Parameters = [first, second],
            Reason = "The pinned server refuses the pair.",
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
