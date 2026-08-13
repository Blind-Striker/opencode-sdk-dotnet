using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Collapses curated schema aliases before binding: every reference to an aliased key is
/// rewritten to its target and the aliased schema leaves the graph, so downstream machinery
/// never sees the duplicate. Container kinds without a rewrite rule pass through unchanged;
/// a reference they might carry then dangles and fails reachability loudly.
/// </summary>
internal sealed class SchemaAliasApplier
{
    public SpecDocument Apply(SpecDocument document, IReadOnlyList<SchemaAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(aliases);

        if (aliases.Count is 0)
        {
            return document;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            map[alias.Schema] = alias.AliasOf;
        }

        var schemas = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
        foreach (var (key, schema) in document.Schemas)
        {
            if (!map.ContainsKey(key))
            {
                schemas[key] = Rewrite(schema, map);
            }
        }

        return new SpecDocument
        {
            Operations = [.. document.Operations.Select(operation => RewriteOperation(operation, map))],
            Schemas = schemas,
        };
    }

    private SpecOperation RewriteOperation(SpecOperation operation, Dictionary<string, string> map) =>
        operation with
        {
            Parameters = [.. operation.Parameters.Select(parameter => parameter with { Schema = Rewrite(parameter.Schema, map) })],
            RequestBody = operation.RequestBody is null
                ? null
                : operation.RequestBody with { Schema = Rewrite(operation.RequestBody.Schema, map) },
            Responses =
            [
                .. operation.Responses.Select(response => response.Schema is null
                    ? response
                    : response with { Schema = Rewrite(response.Schema, map) }),
            ],
        };

    private SchemaNode Rewrite(SchemaNode node, Dictionary<string, string> map) => node switch
    {
        RefNode reference when map.TryGetValue(reference.Target, out var target) => reference with { Target = target },
        NullableNode nullable => nullable with { Inner = Rewrite(nullable.Inner, map) },
        ArrayNode array => array with { Item = Rewrite(array.Item, map) },
        TupleNode tuple => tuple with { Items = [.. tuple.Items.Select(item => Rewrite(item, map))] },
        DictionaryNode dictionary => dictionary with { Value = Rewrite(dictionary.Value, map) },
        UnionNode union => RewriteUnion(union, map),
        ObjectNode target => target with
        {
            Properties = [.. target.Properties.Select(property => property with { Schema = Rewrite(property.Schema, map) })],
            AdditionalPropertiesSchema = target.AdditionalPropertiesSchema is null
                ? null
                : Rewrite(target.AdditionalPropertiesSchema, map),
        },
        _ => node,
    };

    /// <summary>Collapsed branches can duplicate a reference; the duplicates fold away mechanically.</summary>
    private UnionNode RewriteUnion(UnionNode union, Dictionary<string, string> map)
    {
        var branches = new List<SchemaNode>(union.Branches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in union.Branches.Select(entry => Rewrite(entry, map)))
        {
            if (branch is RefNode reference && !seen.Add(reference.Target))
            {
                continue;
            }

            branches.Add(branch);
        }

        return union with { Branches = branches };
    }
}
