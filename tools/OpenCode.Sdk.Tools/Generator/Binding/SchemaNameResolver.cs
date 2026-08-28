using System.Collections.ObjectModel;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SchemaNameResolver
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public IReadOnlyDictionary<string, string> Resolve(SpecDocument document, ReachableSchemaSet reachable,
        IReadOnlyList<SpecOperation> selected, GenerationCuration curation, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(errors);

        var responseRoots = reachable.ResponseRootKeys.ToHashSet(_comparer);
        var requestRoots = ResolveRequestBodyRootNames(selected, errors);
        var payloadRoots = ResolveEnvelopePayloadRootNames(selected);
        var effectStreamTypes = ResolveEffectStreamTypeNames(document, selected);
        var artifacts = new ProjectionArtifactNamePolicy(document.Schemas.Keys);
        var curatedNames = curation
            .SchemaNames
            .DistinctBy(static row => row.Schema, StringComparer.Ordinal)
            .ToDictionary(static row => row.Schema, static row => row.DotNetName, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(_comparer);
        var owners = new Dictionary<string, string>(_comparer);
        foreach (var key in reachable.GraphKeys)
        {
            if (responseRoots.Contains(key) || !document.Schemas.TryGetValue(key, out var schema)
                                            || !IsNominal(schema, document.Schemas))
            {
                continue;
            }

            string name;
            if (curatedNames.TryGetValue(key, out var curatedName))
            {
                name = curatedName;
            }
            else if (effectStreamTypes.TryGetValue(key, out var effectStreamName))
            {
                name = effectStreamName;
            }
            else if (requestRoots.TryGetValue(key, out var requestName))
            {
                name = requestName;
            }
            else if (payloadRoots.TryGetValue(key, out var payloadName))
            {
                name = payloadName;
            }
            else
            {
                name = schema switch
                {
                    UnionNode { Classification: UnionClassification.Marked } union =>
                        CSharpNamePolicy.ToUnionInterfaceName(ResolveUnionName(key, union, document.Schemas, artifacts)),
                    UnionNode union => ResolveUnionName(key, union, document.Schemas, artifacts),
                    _ => ResolveDefault(key, artifacts),
                };
            }

            if (owners.TryGetValue(name, out var existing))
            {
                errors.Add(BindingErrorCategory.Naming, key, $"C# type name '{name}' collides with schema '{existing}'");
                continue;
            }

            owners.Add(name, key);
            result.Add(key, name);
        }

        return new ReadOnlyDictionary<string, string>(result);
    }

    /// <summary>
    /// The extension gives its cause graph a shared semantic role independent of the operation
    /// carrying it. The pin currently exposes one such contract; a second selected contract
    /// claiming the same names would hit the ordinary owner collision below rather than silently
    /// merging distinct schemas.
    /// </summary>
    private static Dictionary<string, string> ResolveEffectStreamTypeNames(SpecDocument document,
        IReadOnlyList<SpecOperation> selected)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var extension in selected
                     .SelectMany(static operation => operation.Responses)
                     .Select(static response => response.EffectStream)
                     .OfType<SpecEffectStreamContract>())
        {
            if (extension.CauseSchema is not ArrayNode { Item: RefNode item }
                || !document.Schemas.TryGetValue(item.Target, out var itemSchema)
                || itemSchema is not UnionNode { Classification: UnionClassification.Marked } union)
            {
                continue;
            }

            names[item.Target] = EffectStreamTypeNamePolicy.CauseInterface;
            foreach (var target in union.Branches.OfType<RefNode>().Select(static branch => branch.Target))
            {
                if (!document.Schemas.TryGetValue(target, out var branchSchema)
                    || branchSchema is not ObjectNode objectNode
                    || objectNode.LiteralMarkers.FirstOrDefault(static marker => marker.PropertyName is "_tag") is not { } marker)
                {
                    continue;
                }

                names[target] = EffectStreamTypeNamePolicy.CauseVariant(marker.Value);
            }
        }

        return names;
    }

    /// <summary>
    /// A union that carries no choice is not a type of its own — it binds to what its branches
    /// already are (<see cref="UnstructuredUnionPolicy"/>), so it never claims a C# name.
    /// </summary>
    private static bool IsNominal(SchemaNode schema, IReadOnlyDictionary<string, SchemaNode> graph) => schema switch
    {
        UnionNode { Classification: UnionClassification.Structural } union => UnstructuredUnionPolicy.Collapse(union, graph) is null,
        ObjectNode or EnumNode or UnionNode => true,
        _ => false,
    };

    /// <summary>
    /// Every selected operation's nominal body root — inline or component-referenced — is
    /// named from its operation identity, the mechanical <c>{Subject}{Verb}Request</c> rule
    /// (public names never surface raw operation ids or dotted component spellings).
    /// Ownership is scoped to the selection so a pending operation can never rename a
    /// component the selected closure shares; two selected operations claiming one root
    /// under different names refuse. Nested component dependencies stay shared: only the
    /// root reference is claimed.
    /// </summary>
    private Dictionary<string, string> ResolveRequestBodyRootNames(IReadOnlyList<SpecOperation> selected,
        BindingErrorCollector errors)
    {
        var names = new Dictionary<string, string>(_comparer);
        foreach (var operation in selected)
        {
            if (operation.RequestBody?.Schema is not RefNode reference)
            {
                continue;
            }

            var requestName = OperationNamePolicy.RequestTypeName(operation);
            if (names.TryGetValue(reference.Target, out var existing) && !_comparer.Equals(existing, requestName))
            {
                errors.Add(
                    BindingErrorCategory.Naming,
                    reference.Target,
                    $"request body root is claimed as both '{existing}' and '{requestName}' by selected operations");
                continue;
            }

            names[reference.Target] = requestName;
        }

        return names;
    }

    /// <summary>
    /// A bare success body that ingestion promoted into the graph is the operation's payload
    /// model, and it carries no schema identity of its own: the root it was promoted under is
    /// the operation, which the public surface never spells. Such a payload is named from the
    /// operation instead, mechanically (<see cref="OperationNamePolicy.PayloadTypeName"/>);
    /// a component-referenced payload keeps its component identity, and a reasoned
    /// <c>schemaNames</c> row still overrides. The key embeds the operation id, so no two
    /// selected operations can claim the same one; two claiming one <em>name</em> collide at
    /// the ordinary owner wall in <see cref="Resolve"/>.
    /// </summary>
    private Dictionary<string, string> ResolveEnvelopePayloadRootNames(IReadOnlyList<SpecOperation> selected)
    {
        var names = new Dictionary<string, string>(_comparer);
        foreach (var operation in selected)
        {
            if (operation.Responses.FirstOrDefault(static response => response.StatusCode is 200) is
                {
                    IsSse: false, EnvelopeShape: SpecEnvelopeShape.Bare, Schema: RefNode reference,
                }
                && reference.Target.Contains('#', StringComparison.Ordinal))
            {
                names[reference.Target] = OperationNamePolicy.PayloadTypeName(operation);
            }
        }

        return names;
    }

    private string ResolveUnionName(string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph,
        ProjectionArtifactNamePolicy artifacts)
    {
        if (!key.Contains('#', StringComparison.Ordinal))
        {
            return ResolveDefault(key, artifacts);
        }

        var branchNames = union
            .Branches
            .OfType<RefNode>()
            .Where(reference => graph.ContainsKey(reference.Target))
            .Select(reference => ResolveDefault(reference.Target, artifacts))
            .ToArray();
        if (branchNames.Length != union.Branches.Count)
        {
            return ResolveDefault(key, artifacts);
        }

        var commonWords = CSharpNamePolicy.SplitWords(branchNames[0]).ToArray();
        foreach (var branchName in branchNames.Skip(1))
        {
            var words = CSharpNamePolicy.SplitWords(branchName);
            var commonLength = 0;
            while (commonLength < commonWords.Length && commonLength < words.Count
                                                     && StringComparer.OrdinalIgnoreCase.Equals(commonWords[commonLength], words[commonLength]))
            {
                commonLength++;
            }

            commonWords = commonWords[..commonLength];
        }

        var owner = CSharpNamePolicy.ToPascalCase(GetRoot(key));
        var common = string.Concat(commonWords.Select(CSharpNamePolicy.ToPascalCase));
        return common.Length > 0 && !_comparer.Equals(common, owner)
            ? common
            : ResolveDefault(key, artifacts);
    }

    private static string ResolveDefault(string key, ProjectionArtifactNamePolicy artifacts)
    {
        var root = GetRoot(key);
        var result = new System.Text.StringBuilder(CSharpNamePolicy.ToPascalCase(NormalizeRoot(root, artifacts)));
        var hash = key.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
        {
            return result.ToString();
        }

        var segments = key[(hash + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        while (index < segments.Length - 1)
        {
            if (string.Equals(segments[index], "properties", StringComparison.Ordinal))
            {
                _ = result.Append(CSharpNamePolicy.ToPascalCase(DecodePointer(segments[index + 1])));
                index += 2;
                continue;
            }

            // A promoted union branch is keyed by its marker ("anyOf/type=inline") or, for
            // unmarked branches, by ordinal; the branch name appends the marker value so
            // sibling branches (and their root) never collide: Prompt.FileSource#/anyOf/
            // type=inline -> PromptFileSourceInline.
            if (string.Equals(segments[index], "anyOf", StringComparison.Ordinal)
                || string.Equals(segments[index], "oneOf", StringComparison.Ordinal))
            {
                var branch = DecodePointer(segments[index + 1]);
                var separator = branch.IndexOf('=', StringComparison.Ordinal);
                _ = result.Append(CSharpNamePolicy.ToPascalCase(separator >= 0 ? branch[(separator + 1)..] : branch));
                index += 2;
                continue;
            }

            index++;
        }

        return result.ToString();
    }

    private static string GetRoot(string key)
    {
        var hash = key.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? key : key[..hash];
    }

    /// <summary>
    /// Operation-scoped roots carry no component identity, so artifact suffixes apply only to
    /// component roots.
    /// </summary>
    private static string NormalizeRoot(string root, ProjectionArtifactNamePolicy artifacts) =>
        root.StartsWith("op:", StringComparison.Ordinal) ? root[3..] : artifacts.Normalize(root);

    private static string DecodePointer(string segment) => segment
        .Replace("~1", "/", StringComparison.Ordinal)
        .Replace("~0", "~", StringComparison.Ordinal);
}
