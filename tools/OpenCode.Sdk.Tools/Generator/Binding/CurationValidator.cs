using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class CurationValidator
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public void Validate(SpecDocument document, IReadOnlyList<SpecOperation> selected, ReachableSchemaSet reachable,
        GenerationCuration curation, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(errors);

        var selectedIds = selected.Select(static operation => operation.OperationId).ToHashSet(_comparer);
        var selectedGroups = selected.Select(GetGroup).ToHashSet(_comparer);
        var documentIds = document.Operations.Select(static operation => operation.OperationId).ToHashSet(_comparer);
        var documentGroups = document.Operations.Select(GetGroup).ToHashSet(_comparer);
        ValidateGroups(selected, selectedGroups, documentGroups, curation, errors);
        ValidateEnvelopeNames(selectedIds, documentIds, curation, errors);
        ValidatePropertyOverrides(document, reachable, curation, errors);
        ValidateSchemaAliases(document, reachable, curation, errors);
    }

    private static void ValidateGroups(IReadOnlyList<SpecOperation> selected, HashSet<string> selectedGroups,
        HashSet<string> documentGroups, GenerationCuration curation, BindingErrorCollector errors)
    {
        foreach (var group in selectedGroups.Where(group => !curation.Groups.ContainsKey(group)).Order(StringComparer.Ordinal))
        {
            errors.Add(BindingErrorCategory.Curation, group, "selected operation group has no curation row");
        }

        foreach (var (wireName, group) in curation.Groups.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!documentGroups.Contains(wireName))
            {
                errors.Add(BindingErrorCategory.Curation, wireName, "curated group does not exist in the spec");
            }
            else if (!selectedGroups.Contains(wireName))
            {
                errors.Add(BindingErrorCategory.Curation, wireName, "curated group is not selected by the current profile");
            }

            ValidateGroupShape(wireName, group, selected, errors);
        }

        // Groups sharing a client name merge into one client family, so their handle
        // declarations must agree exactly — a divergent row would fork the family.
        foreach (var clientName in curation.Groups
                     .Where(static pair => pair.Value is { Placement: GroupPlacement.Client, ClientName: not null })
                     .GroupBy(static pair => pair.Value.ClientName!, StringComparer.Ordinal)
                     .Where(static family => family
                         .Select(static pair => (pair.Value.HandleName, pair.Value.HandleParameter))
                         .Distinct()
                         .Skip(1)
                         .Any())
                     .Select(static family => family.Key)
                     .Order(StringComparer.Ordinal))
        {
            errors.Add(
                BindingErrorCategory.Curation,
                clientName,
                $"groups sharing client '{clientName}' must declare identical handle configuration");
        }
    }

    private static void ValidateGroupShape(string wireName, GroupCuration group, IReadOnlyList<SpecOperation> selected, BindingErrorCollector errors)
    {
        switch (group.Placement)
        {
            case GroupPlacement.Root when group.ClientName is not null || group.HandleName is not null || group.HandleParameter is not null:
                errors.Add(BindingErrorCategory.Curation, wireName, "root group cannot declare clientName, handleName, or handleParameter");
                break;
            case GroupPlacement.Client when string.IsNullOrWhiteSpace(group.ClientName):
                errors.Add(BindingErrorCategory.Curation, wireName, "client group must declare clientName");
                break;
            case not (GroupPlacement.Root or GroupPlacement.Client):
                // System.Text.Json admits numeric enum spellings even under the string
                // converter, so an out-of-range placement must fail here, not drop silently.
                errors.Add(
                    BindingErrorCategory.Curation,
                    wireName,
                    $"placement value '{((int)group.Placement).ToString(System.Globalization.CultureInfo.InvariantCulture)}' is not a recognized group placement");
                break;
        }

        if (group.ClientName is not null && !CSharpNamePolicy.IsValidIdentifier(group.ClientName))
        {
            errors.Add(BindingErrorCategory.Naming, wireName, $"client name '{group.ClientName}' is not a valid C# identifier");
        }

        if (group.HandleName is not null && !CSharpNamePolicy.IsValidIdentifier(group.HandleName))
        {
            errors.Add(BindingErrorCategory.Naming, wireName, $"handle name '{group.HandleName}' is not a valid C# identifier");
        }

        if ((group.HandleName is not null) != (group.HandleParameter is not null))
        {
            errors.Add(BindingErrorCategory.Curation, wireName, "handleName and handleParameter must be declared together");
            return;
        }

        if (group.HandleParameter is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(group.HandleParameter))
        {
            errors.Add(BindingErrorCategory.Curation, wireName, "handleParameter cannot be blank");
            return;
        }

        ValidateHandleParameterCoverage(wireName, group, selected, errors);
    }

    /// <summary>
    /// Coverage is selection-scoped: during staged generation only selected operations can
    /// witness the handle parameter, and a group with no selected operations already fails
    /// the global orphan check.
    /// </summary>
    private static void ValidateHandleParameterCoverage(string wireName, GroupCuration group, IReadOnlyList<SpecOperation> selected,
        BindingErrorCollector errors)
    {
        if (group.Placement is not GroupPlacement.Client)
        {
            return;
        }

        var groupOperations = selected
            .Where(operation => string.Equals(GetGroup(operation), wireName, StringComparison.Ordinal))
            .ToArray();
        if (groupOperations.Length is 0)
        {
            return;
        }

        var covered = groupOperations.Any(operation => operation.Parameters.Any(parameter => parameter is
        {
            Location: SpecParameterLocation.Path,
            IsRequired: true,
        } && string.Equals(parameter.Name, group.HandleParameter, StringComparison.Ordinal)));
        if (!covered)
        {
            errors.Add(
                BindingErrorCategory.Curation,
                wireName,
                $"handle parameter '{group.HandleParameter}' does not name a required path parameter on any selected operation in the group");
        }
    }

    /// <summary>
    /// Payload names derive mechanically from the operation subject; curated entries are
    /// overrides only (maintainer, 2026-08-13), so validation covers orphans and identifier
    /// legality, never presence.
    /// </summary>
    private static void ValidateEnvelopeNames(HashSet<string> selectedIds, HashSet<string> documentIds,
        GenerationCuration curation, BindingErrorCollector errors)
    {
        foreach (var (operationId, payloadName) in curation.EnvelopePayloadNames.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!documentIds.Contains(operationId))
            {
                errors.Add(BindingErrorCategory.Curation, operationId, "curated operation does not exist in the spec");
            }
            else if (!selectedIds.Contains(operationId))
            {
                errors.Add(BindingErrorCategory.Curation, operationId, "curated operation is not selected by the current profile");
            }

            if (!CSharpNamePolicy.IsValidIdentifier(payloadName))
            {
                errors.Add(BindingErrorCategory.Naming, operationId, $"payload name '{payloadName}' is not a valid C# identifier");
            }
        }
    }

    private static void ValidatePropertyOverrides(SpecDocument document, ReachableSchemaSet reachable, GenerationCuration curation, BindingErrorCollector errors)
    {
        var reachableKeys = reachable.GraphKeys.ToHashSet(StringComparer.Ordinal);
        var targets = new HashSet<(string Schema, string Property)>();
        foreach (var propertyOverride in curation.PropertyOverrides)
        {
            var subject = $"{propertyOverride.Schema}.{propertyOverride.Property}";
            if (!targets.Add((propertyOverride.Schema, propertyOverride.Property)))
            {
                errors.Add(BindingErrorCategory.Curation, subject, "property override is duplicated");
            }

            if (string.IsNullOrWhiteSpace(propertyOverride.Reason))
            {
                errors.Add(BindingErrorCategory.Curation, subject, "property override must declare a reason");
            }

            if (!document.Schemas.TryGetValue(propertyOverride.Schema, out var schema))
            {
                errors.Add(BindingErrorCategory.Curation, subject, "curated schema does not exist in the spec");
                continue;
            }

            if (!reachableKeys.Contains(propertyOverride.Schema))
            {
                errors.Add(BindingErrorCategory.Curation, subject, "curated property is not selected by the current profile");
            }

            if (schema is not ObjectNode objectSchema
                || !objectSchema.Properties.Any(property => string.Equals(property.Name, propertyOverride.Property, StringComparison.Ordinal)))
            {
                errors.Add(BindingErrorCategory.Curation, subject, "curated property does not exist on the schema");
            }
        }
    }

    /// <summary>
    /// The alias walls carry the drift contract: a deleted source or target orphans the row,
    /// a dereferenced source goes dormant, and any structural divergence — the tag included —
    /// breaks the identity check. Every upstream move on the duplicate is loud.
    /// </summary>
    private static void ValidateSchemaAliases(SpecDocument document, ReachableSchemaSet reachable,
        GenerationCuration curation, BindingErrorCollector errors)
    {
        var reachableKeys = reachable.GraphKeys.ToHashSet(StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in curation.SchemaAliases)
        {
            if (!sources.Add(alias.Schema))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, "schema alias is duplicated");
            }

            if (string.IsNullOrWhiteSpace(alias.Reason))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, "schema alias must declare a reason");
            }

            if (string.Equals(alias.Schema, alias.AliasOf, StringComparison.Ordinal))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, "schema alias cannot target itself");
                continue;
            }

            if (!document.Schemas.TryGetValue(alias.Schema, out var source))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, "aliased schema does not exist in the spec");
                continue;
            }

            if (!document.Schemas.TryGetValue(alias.AliasOf, out var target))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, $"alias target '{alias.AliasOf}' does not exist in the spec");
                continue;
            }

            if (!reachableKeys.Contains(alias.Schema))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, "aliased schema is not referenced by the selected profile");
            }

            if (!SchemaNodeComparer.DeepEquals(source, target))
            {
                errors.Add(BindingErrorCategory.Curation, alias.Schema, "aliased schemas must be structurally identical");
            }
        }

        foreach (var alias in curation.SchemaAliases
                     .Where(alias => !string.Equals(alias.Schema, alias.AliasOf, StringComparison.Ordinal)
                                     && sources.Contains(alias.AliasOf))
                     .OrderBy(static alias => alias.Schema, StringComparer.Ordinal))
        {
            errors.Add(BindingErrorCategory.Curation, alias.Schema, "schema aliases cannot chain");
        }
    }

    private static string GetGroup(SpecOperation operation) => operation.Segments[0];
}
