using System.Collections.ObjectModel;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SchemaNameResolver
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public IReadOnlyDictionary<string, string> Resolve(SpecDocument document, ReachableSchemaSet reachable,
        IReadOnlyList<SpecOperation> selected, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(errors);

        var responseRoots = reachable.ResponseRootKeys.ToHashSet(_comparer);
        var requestRoots = ResolveRequestBodyRootNames(selected, errors);
        var result = new Dictionary<string, string>(_comparer);
        var owners = new Dictionary<string, string>(_comparer);
        foreach (var key in reachable.GraphKeys)
        {
            if (responseRoots.Contains(key) || !document.Schemas.TryGetValue(key, out var schema) || !IsNominal(schema))
            {
                continue;
            }

            string name;
            if (requestRoots.TryGetValue(key, out var requestName))
            {
                name = requestName;
            }
            else
            {
                name = schema is UnionNode union
                    ? CSharpNamePolicy.ToUnionInterfaceName(ResolveUnionName(key, union, document.Schemas))
                    : ResolveDefault(key);
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

    private static bool IsNominal(SchemaNode schema) => schema is ObjectNode or EnumNode or UnionNode;

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

    private string ResolveUnionName(string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph)
    {
        if (!key.Contains('#', StringComparison.Ordinal))
        {
            return ResolveDefault(key);
        }

        var branchNames = union.Branches
            .OfType<RefNode>()
            .Where(reference => graph.ContainsKey(reference.Target))
            .Select(reference => ResolveDefault(reference.Target))
            .ToArray();
        if (branchNames.Length != union.Branches.Count)
        {
            return ResolveDefault(key);
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
            : ResolveDefault(key);
    }

    private static string ResolveDefault(string key)
    {
        var root = GetRoot(key);
        var result = new System.Text.StringBuilder(CSharpNamePolicy.ToPascalCase(NormalizeRoot(root)));
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

    private static string NormalizeRoot(string root) => root.StartsWith("op:", StringComparison.Ordinal) ? root[3..] : root;

    private static string DecodePointer(string segment) => segment
        .Replace("~1", "/", StringComparison.Ordinal)
        .Replace("~0", "~", StringComparison.Ordinal);
}
