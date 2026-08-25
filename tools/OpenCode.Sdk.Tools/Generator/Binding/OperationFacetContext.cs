using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Read-only per-operation state shared by the facet binders: the ingested document, the
/// operation under bind, its curation, the resolved type-name map, and the error batch.
/// It also owns the one ref-graph <see cref="Resolve"/> and the refusal helpers, so every
/// facet attributes its refusals to the same operation identically.
/// </summary>
internal sealed class OperationFacetContext(
    SpecDocument document,
    SpecOperation operation,
    GenerationCuration curation,
    IReadOnlyDictionary<string, string> typeNames,
    BindingErrorCollector errors)
{
    public GenerationCuration Curation { get; } = curation ?? throw new ArgumentNullException(nameof(curation));

    public SpecDocument Document { get; } = document ?? throw new ArgumentNullException(nameof(document));

    public BindingErrorCollector Errors { get; } = errors ?? throw new ArgumentNullException(nameof(errors));

    public SpecOperation Operation { get; } = operation ?? throw new ArgumentNullException(nameof(operation));

    public IReadOnlyDictionary<string, string> TypeNames { get; } = typeNames ?? throw new ArgumentNullException(nameof(typeNames));

    public void Refuse(string problem) => Errors.Add(BindingErrorCategory.Operation, Operation.OperationId, problem);

    public string? RefuseNull(string problem)
    {
        Refuse(problem);
        return null;
    }

    public T? RefuseNull<T>(string problem)
        where T : class
    {
        Refuse(problem);
        return null;
    }

    public SchemaNode Resolve(SchemaNode schema)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = schema;
        while (current is RefNode reference && visited.Add(reference.Target)
                                            && Document.Schemas.TryGetValue(reference.Target, out var target))
        {
            current = target;
        }

        return current;
    }
}
