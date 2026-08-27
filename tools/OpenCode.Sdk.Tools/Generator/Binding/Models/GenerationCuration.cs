using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record GenerationCuration
{
    [JsonPropertyName("groups")]
    public required IReadOnlyDictionary<string, GroupCuration> Groups
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = new ReadOnlyDictionary<string, GroupCuration>(new Dictionary<string, GroupCuration>(value, StringComparer.Ordinal));
        }
    } = new ReadOnlyDictionary<string, GroupCuration>(new Dictionary<string, GroupCuration>(StringComparer.Ordinal));

    [JsonPropertyName("operationIdentities")]
    public required IReadOnlyList<OperationIdentityCuration> OperationIdentities
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<OperationIdentityCuration>());

    [JsonPropertyName("operationNames")]
    public required IReadOnlyList<OperationNameCuration> OperationNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<OperationNameCuration>());

    [JsonPropertyName("schemaNames")]
    public required IReadOnlyList<SchemaNameCuration> SchemaNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<SchemaNameCuration>());

    [JsonPropertyName("envelopePayloadNames")]
    public required IReadOnlyDictionary<string, string> EnvelopePayloadNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
        }
    } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    [JsonPropertyName("schemaAliases")]
    public required IReadOnlyList<SchemaAlias> SchemaAliases
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<SchemaAlias>());

    /// <summary>Fingerprints for operations that are never selected but that hand-written code depends on (ADR-0021).</summary>
    [JsonPropertyName("transportOwned")]
    public required IReadOnlyList<TransportOwnedCuration> TransportOwned
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<TransportOwnedCuration>());
}
