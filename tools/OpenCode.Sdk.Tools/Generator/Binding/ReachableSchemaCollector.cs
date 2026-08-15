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
            // Query parameter schemas never generate models: the options binder consumes
            // them shape-wise and fails closed on anything it does not recognize.
            foreach (var parameter in operation.Parameters
                         .Where(static parameter => parameter.Location is SpecParameterLocation.Path))
            {
                traversal.Visit(parameter.Schema);
            }

            if (operation.RequestBody is not null)
            {
                traversal.Visit(operation.RequestBody.Schema);
            }

            foreach (var response in operation.Responses)
            {
                traversal.VisitResponse(operation.OperationId, response);
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

        public void VisitResponse(string operationId, SpecResponse response)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
            ArgumentNullException.ThrowIfNull(response);

            var schema = response.Schema;
            if (schema is RefNode reference)
            {
                // Data-carrying wrappers are envelope structure, never models — component-named
                // wrappers join the op-inline roots in the excluded set.
                var isInlineRoot = reference.Target.StartsWith($"op:{operationId}#", StringComparison.Ordinal);
                var isWrapperShape = response.EnvelopeShape
                    is SpecEnvelopeShape.Data or SpecEnvelopeShape.CursorData or SpecEnvelopeShape.DataLocation;
                if (isInlineRoot || isWrapperShape)
                {
                    _ = _responseRoots.Add(reference.Target);
                }

                if (response.EnvelopeShape is SpecEnvelopeShape.CursorData)
                {
                    ExcludeCursorObject(reference.Target);
                }
            }

            if (schema is not null)
            {
                Visit(schema);
            }
        }

        /// <summary>The cursor object rides the envelope contract (hand-written spine), never the model closure.</summary>
        private void ExcludeCursorObject(string wrapperKey)
        {
            if (!_graph.TryGetValue(wrapperKey, out var wrapperSchema) || wrapperSchema is not ObjectNode wrapper)
            {
                return;
            }

            foreach (var property in wrapper.Properties)
            {
                if (property is { Name: "cursor", Schema: RefNode cursorReference })
                {
                    _ = _responseRoots.Add(cursorReference.Target);
                }
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
