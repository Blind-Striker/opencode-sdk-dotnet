using System.Collections.ObjectModel;
using OpenCode.Sdk.Tools.Generator.Binding.Abstractions;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SpecBinder(
    ReachableSchemaCollector reachableSchemas,
    CurationValidator curationValidator,
    SchemaAliasApplier schemaAliases,
    SchemaNameResolver schemaNames,
    SchemaPlanBinder schemaPlans,
    OperationPlanBinder operationPlans) : ISpecBinder
{
    private readonly CurationValidator _curationValidator = curationValidator ?? throw new ArgumentNullException(nameof(curationValidator));
    private readonly OperationPlanBinder _operationPlans = operationPlans ?? throw new ArgumentNullException(nameof(operationPlans));
    private readonly ReachableSchemaCollector _reachableSchemas = reachableSchemas ?? throw new ArgumentNullException(nameof(reachableSchemas));
    private readonly SchemaAliasApplier _schemaAliases = schemaAliases ?? throw new ArgumentNullException(nameof(schemaAliases));
    private readonly SchemaNameResolver _schemaNames = schemaNames ?? throw new ArgumentNullException(nameof(schemaNames));
    private readonly SchemaPlanBinder _schemaPlans = schemaPlans ?? throw new ArgumentNullException(nameof(schemaPlans));

    public EmitPlan Bind(SpecDocument document, OperationSelection selection, GenerationCuration curation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(curation);

        var errors = new BindingErrorCollector();
        var operationsById = document.Operations.ToDictionary(static operation => operation.OperationId, StringComparer.Ordinal);
        var selected = SelectOperations(selection, operationsById, errors);
        var reachable = _reachableSchemas.Collect(document, selected, errors);
        _curationValidator.Validate(document, selected, reachable, curation, errors);

        // Aliases are validated against the raw document above; the collapse happens before
        // names resolve so the rest of the pipeline never sees the duplicates.
        var aliased = _schemaAliases.Apply(document, curation.SchemaAliases);
        if (!ReferenceEquals(aliased, document))
        {
            document = aliased;
            var aliasedById = document.Operations.ToDictionary(static operation => operation.OperationId, StringComparer.Ordinal);
            selected = Array.AsReadOnly([.. selected.Select(operation => aliasedById[operation.OperationId])]);
            reachable = MapReachableKeys(reachable, curation.SchemaAliases);
        }

        // Type names are resolved exactly once per bind; schema and operation binding
        // consume the same map so neither can reinterpret a schema under another name.
        var typeNames = _schemaNames.Resolve(document, reachable, errors);
        var schemaResult = _schemaPlans.Bind(document, reachable, curation, typeNames, errors);
        var clients = _operationPlans.Bind(document, selected, curation, typeNames, errors);
        var selectedIds = selected.Select(static operation => operation.OperationId).ToHashSet(StringComparer.Ordinal);
        var pending = document.Operations
            .Where(operation => !selectedIds.Contains(operation.OperationId))
            .OrderBy(static operation => operation.OperationId, StringComparer.Ordinal)
            .Select(static operation => new PendingOperationPlan
            {
                OperationId = operation.OperationId,
            })
            .ToArray();

        errors.ThrowIfAny();
        return new EmitPlan
        {
            SelectedOperationIds = selection.OperationIds,
            Models = schemaResult.Models,
            Unions = schemaResult.Unions,
            Registry = schemaResult.Registry,
            Clients = clients,
            PendingOperations = pending,
        };
    }

    /// <summary>
    /// The post-collapse reachable set follows from the raw one mechanically: aliased keys
    /// vanish and their targets take their place, so no second traversal (and no duplicated
    /// traversal diagnostics) is needed.
    /// </summary>
    private static ReachableSchemaSet MapReachableKeys(ReachableSchemaSet reachable, IReadOnlyList<SchemaAlias> aliases)
    {
        // Tolerant of duplicated sources: the validator has already recorded them and the
        // batched failure throws before any plan leaves this bind.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            map[alias.Schema] = alias.AliasOf;
        }
        return new ReachableSchemaSet
        {
            GraphKeys = [.. reachable.GraphKeys
                .Select(key => map.GetValueOrDefault(key, key))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)],
            ResponseRootKeys = [.. reachable.ResponseRootKeys
                .Select(key => map.GetValueOrDefault(key, key))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)],
        };
    }

    private static ReadOnlyCollection<SpecOperation> SelectOperations(OperationSelection selection,
        Dictionary<string, SpecOperation> operationsById, BindingErrorCollector errors)
    {
        var selected = new List<SpecOperation>(selection.OperationIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operationId in selection.OperationIds)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                errors.Add(BindingErrorCategory.Selection, "profile", "selected operation ID cannot be blank");
                continue;
            }

            if (!seen.Add(operationId))
            {
                errors.Add(BindingErrorCategory.Selection, operationId, "selected operation ID is duplicated");
                continue;
            }

            if (!operationsById.TryGetValue(operationId, out var operation))
            {
                errors.Add(BindingErrorCategory.Selection, operationId, "selected operation does not exist in the ingested spec");
                continue;
            }

            selected.Add(operation);
        }

        return Array.AsReadOnly([.. selected]);
    }
}
