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
        ValidateEnvelopeNames(selected, selectedIds, documentIds, curation, errors);
        ValidatePropertyOverrides(document, reachable, curation, errors);
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
    }

    private static void ValidateGroupShape(string wireName, GroupCuration group, IReadOnlyList<SpecOperation> selected, BindingErrorCollector errors)
    {
        switch (group.Placement)
        {
            case GroupPlacement.Root when group.ClientName is not null || group.HandleName is not null:
                errors.Add(BindingErrorCategory.Curation, wireName, "root group cannot declare clientName or handleName");
                break;
            case GroupPlacement.Client when string.IsNullOrWhiteSpace(group.ClientName):
                errors.Add(BindingErrorCategory.Curation, wireName, "client group must declare clientName");
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

        var requiresSessionHandle = selected.Any(operation => string.Equals(GetGroup(operation), wireName, StringComparison.Ordinal)
            && operation.Parameters.Any(static parameter => parameter is
            {
                Name: "sessionID",
                Location: SpecParameterLocation.Path,
            }));
        if (requiresSessionHandle && string.IsNullOrWhiteSpace(group.HandleName))
        {
            errors.Add(BindingErrorCategory.Curation, wireName, "session-scoped selected operation requires handleName");
        }
    }

    private static void ValidateEnvelopeNames(IReadOnlyList<SpecOperation> selected, HashSet<string> selectedIds,
        HashSet<string> documentIds, GenerationCuration curation, BindingErrorCollector errors)
    {
        foreach (var operation in selected)
        {
            var requiresPayloadName = operation.Responses.Any(static response => response.StatusCode is >= 200 and < 300
                && response.EnvelopeShape is SpecEnvelopeShape.Data or SpecEnvelopeShape.DataLocation
                    or SpecEnvelopeShape.CursorData or SpecEnvelopeShape.DataHasMore);
            if (requiresPayloadName && !curation.EnvelopePayloadNames.ContainsKey(operation.OperationId))
            {
                errors.Add(BindingErrorCategory.Curation, operation.OperationId, "selected response envelope has no payload name");
            }
        }

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

    private static string GetGroup(SpecOperation operation) => operation.Segments[0];
}
