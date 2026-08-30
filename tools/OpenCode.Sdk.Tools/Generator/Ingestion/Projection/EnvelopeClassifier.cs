using System.Collections.Frozen;
using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class EnvelopeClassifier
{
    private readonly FrozenSet<string> _cursorData = new[] { "cursor", "data", }.ToFrozenSet(StringComparer.Ordinal);
    private readonly FrozenSet<string> _data = new[] { "data", }.ToFrozenSet(StringComparer.Ordinal);
    private readonly FrozenSet<string> _dataHasMore = new[] { "data", "hasMore", }.ToFrozenSet(StringComparer.Ordinal);
    private readonly FrozenSet<string> _dataLocation = new[] { "data", "location", }.ToFrozenSet(StringComparer.Ordinal);

    public SpecEnvelopeShape Classify(SpecMediaType? contentType, IOpenApiSchema? schema, string location, IngestionErrorCollector errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        if (contentType is null)
        {
            return SpecEnvelopeShape.None;
        }

        if (!contentType.IsJson || schema is null)
        {
            return SpecEnvelopeShape.Bare;
        }

        var concrete = ChaseReference(schema, location, errors);
        if (concrete?.Properties is not { } properties)
        {
            return SpecEnvelopeShape.Bare;
        }

        var propertyNames = properties.Keys.ToHashSet(StringComparer.Ordinal);
        return properties.Count switch
        {
            1 when propertyNames.SetEquals(_data) => SpecEnvelopeShape.Data,

            // A single non-'data' key is envelope spine only where the operation declares the
            // object inline. A named component with one property is a model with its own
            // identity — Worktree.Info's {directory}, SessionInterruptResponse's {interrupted} —
            // and flattening it would erase a name upstream chose. Requiredness is deliberately
            // not read here: classification is a shape question about keys, and whether the sole
            // property is required is a binding fact that EnvelopeFacetBinder.SingleKeyMember
            // owns and refuses by name. Admitting an optional single property here and refusing
            // it there keeps one wall rather than two that could disagree.
            1 when schema is not OpenApiSchemaReference => SpecEnvelopeShape.SingleKey,
            2 when propertyNames.SetEquals(_dataLocation) => SpecEnvelopeShape.DataLocation,
            2 when propertyNames.SetEquals(_cursorData) => SpecEnvelopeShape.CursorData,
            2 when propertyNames.SetEquals(_dataHasMore) => SpecEnvelopeShape.DataHasMore,
            _ => SpecEnvelopeShape.Bare,
        };
    }

    private static OpenApiSchema? ChaseReference(IOpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);
        var current = schema;
        while (current is OpenApiSchemaReference reference)
        {
            if (!visited.Add(current))
            {
                errors.Add(location, "response envelope schema contains a reference cycle");
                return null;
            }

            if (reference.Target is null)
            {
                errors.Add(location, $"response envelope reference target '{reference.Reference.Id ?? "<missing>"}' could not be resolved");
                return null;
            }

            current = reference.Target;
        }

        if (current is OpenApiSchema concrete)
        {
            return concrete;
        }

        errors.Add(location, $"response envelope schema implementation '{current.GetType().Name}' is not supported");
        return null;
    }
}
