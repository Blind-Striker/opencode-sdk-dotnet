using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class PrefixItemsAdapter
{
    private readonly GraphKeyBuilder _keys;
    private readonly SchemaProjector _schemaProjector;

    public PrefixItemsAdapter(SchemaProjector schemaProjector, GraphKeyBuilder keys)
    {
        _schemaProjector = schemaProjector ?? throw new ArgumentNullException(nameof(schemaProjector));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public TupleNode? Project(OpenApiSchema schema, string root, string pointer, string location, ProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(state);

        if (schema.UnrecognizedKeywords is null
            || !schema.UnrecognizedKeywords.TryGetValue("prefixItems", out var rawPrefixItems)
            || rawPrefixItems is not JsonArray prefixItems)
        {
            state.Errors.Add(location, "prefixItems was admitted but its raw array was unavailable");
            return null;
        }

        if (!ValidateArity(schema, prefixItems.Count, root, pointer, state.Errors))
        {
            return null;
        }

        var items = new List<SchemaNode>(prefixItems.Count);
        for (var index = 0; index < prefixItems.Count; index++)
        {
            var itemPointer = _keys.Append(_keys.Append(pointer, "prefixItems"), index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var itemLocation = string.Concat(root, itemPointer);
            if (prefixItems[index] is not { } rawItem)
            {
                state.Errors.Add(itemLocation, "prefixItems entries cannot be null");
                continue;
            }

            var parsed = OpenApiModelFactory.Parse<OpenApiSchema>(rawItem.ToJsonString(), OpenApiSpecVersion.OpenApi3_1, state.Document,
                out var diagnostic, "json");
            foreach (var error in diagnostic.Errors)
            {
                state.Errors.Add(itemLocation, error.Message);
            }

            if (diagnostic.Errors.Count > 0)
            {
                continue;
            }

            if (parsed is null)
            {
                state.Errors.Add(itemLocation, "prefixItems entry did not produce a schema");
                continue;
            }

            var projected = _schemaProjector.Project(parsed, root, itemPointer, state);
            if (projected is not null)
            {
                items.Add(projected);
            }
        }

        return items.Count != prefixItems.Count
            ? null
            : new TupleNode
            {
                Description = schema.Description,
                Format = schema.Format,
                Items = items,
            };
    }

    private bool ValidateArity(OpenApiSchema schema, int arity, string root, string pointer, IngestionErrorCollector errors)
    {
        var isValid = true;
        var arityText = arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (schema.MinItems != arity)
        {
            errors.Add(string.Concat(root, _keys.Append(pointer, "minItems")), $"minItems must equal tuple arity {arityText}");
            isValid = false;
        }

        if (schema.MaxItems == arity)
        {
            return isValid;
        }

        errors.Add(string.Concat(root, _keys.Append(pointer, "maxItems")), $"maxItems must equal tuple arity {arityText}");

        return false;
    }
}
