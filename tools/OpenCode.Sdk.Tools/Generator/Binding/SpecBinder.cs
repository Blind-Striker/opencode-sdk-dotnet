using System.Collections.ObjectModel;
using OpenCode.Sdk.Tools.Generator.Binding.Abstractions;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SpecBinder(
    ReachableSchemaCollector reachableSchemas,
    CurationValidator curationValidator,
    SchemaNameResolver schemaNames,
    SchemaPlanBinder schemaPlans,
    OperationPlanBinder operationPlans) : ISpecBinder
{
    private readonly CurationValidator _curationValidator = curationValidator ?? throw new ArgumentNullException(nameof(curationValidator));
    private readonly OperationPlanBinder _operationPlans = operationPlans ?? throw new ArgumentNullException(nameof(operationPlans));
    private readonly ReachableSchemaCollector _reachableSchemas = reachableSchemas ?? throw new ArgumentNullException(nameof(reachableSchemas));
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
