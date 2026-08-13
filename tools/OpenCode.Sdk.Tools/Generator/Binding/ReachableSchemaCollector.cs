using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class ReachableSchemaCollector
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public ReachableSchemaSet Collect(SpecDocument document, IReadOnlyList<SpecOperation> operations, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(errors);

        var traversal = new ReachabilityTraversal(document.Schemas, errors, _comparer);
        foreach (var operation in operations)
        {
            foreach (var parameter in operation.Parameters)
            {
                traversal.Visit(parameter.Schema);
            }

            if (operation.RequestBody is not null)
            {
                traversal.Visit(operation.RequestBody.Schema);
            }

            foreach (var response in operation.Responses)
            {
                traversal.VisitResponse(operation.OperationId, response.Schema);
            }
        }

        return traversal.Snapshot();
    }

    private sealed class ReachabilityTraversal(IReadOnlyDictionary<string, SchemaNode> graph, BindingErrorCollector errors, StringComparer comparer)
    {
        private readonly StringComparer _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        private readonly BindingErrorCollector _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        private readonly IReadOnlyDictionary<string, SchemaNode> _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        private readonly HashSet<string> _keys = new(comparer);
        private readonly HashSet<string> _responseRoots = new(comparer);

        public void VisitResponse(string operationId, SchemaNode? schema)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            if (schema is RefNode reference && reference.Target.StartsWith($"op:{operationId}#", StringComparison.Ordinal))
            {
                _ = _responseRoots.Add(reference.Target);
            }

            if (schema is not null)
            {
                Visit(schema);
            }
        }

        public void Visit(SchemaNode schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            if (schema is RefNode reference)
            {
                VisitReference(reference.Target);
                return;
            }

            foreach (var child in schema.Children)
            {
                Visit(child);
            }
        }

        public ReachableSchemaSet Snapshot() =>
            new()
            {
                GraphKeys = [.. _keys.Order(_comparer)],
                ResponseRootKeys = [.. _responseRoots.Order(_comparer)],
            };

        private void VisitReference(string target)
        {
            if (!_keys.Add(target))
            {
                return;
            }

            if (!_graph.TryGetValue(target, out var targetSchema))
            {
                _errors.Add(BindingErrorCategory.Schema, target, "referenced schema is absent from the ingested graph");
                return;
            }

            Visit(targetSchema);
        }
    }
}
