using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class StructuralUnionPlanBinder
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public StructuralUnionModelPlan? Bind(string key, string name, UnionNode union,
        IReadOnlyDictionary<string, SchemaNode> graph, TypePlanBinder typeBinder, BindingErrorCollector errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(union);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(typeBinder);
        ArgumentNullException.ThrowIfNull(errors);

        var arms = new List<StructuralUnionArmPlan>(union.Branches.Count);
        var claimedTokens = new HashSet<JsonTokenType>();
        var inhabitableBranchCount = 0;
        foreach (var (branch, index) in union.Branches.Select(static (branch, index) => (branch, index)))
        {
            var resolved = Resolve(branch, graph, []);
            if (resolved is NeverNode)
            {
                continue;
            }

            inhabitableBranchCount++;

            if (!TryGetTokens(resolved, out var tokens))
            {
                errors.Add(BindingErrorCategory.Schema, key,
                    $"structural union branch {index.ToString(System.Globalization.CultureInfo.InvariantCulture)} has no deterministic JSON-token dispatch");
                continue;
            }

            var effective = ResolveEffectiveBranch(branch, resolved, tokens, claimedTokens, key, index, errors);
            if (effective is null)
            {
                continue;
            }

            var effectiveTokens = tokens.Where(token => !claimedTokens.Contains(token)).ToArray();
            if (!ValidateSpecialNumberArm(resolved, effectiveTokens, key, errors))
            {
                continue;
            }

            var armNameSubject = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var type = typeBinder.BindStructuralArm(key, armNameSubject, effective);
            if (type is null)
            {
                continue;
            }

            var armName = ArmName(type);
            if (!ValidateArmName(armName, arms, key, errors))
            {
                continue;
            }

            arms.Add(new StructuralUnionArmPlan
            {
                Name = armName,
                Type = type,
                Tokens = effectiveTokens,
            });
            claimedTokens.UnionWith(effectiveTokens);
        }

        if (arms.Count < 2 || arms.Count != inhabitableBranchCount)
        {
            if (arms.Count < 2)
            {
                errors.Add(BindingErrorCategory.Schema, key,
                    "structural union must retain at least two inhabitable, distinguishable branches");
            }

            return null;
        }

        return CreatePlan(name, union, arms);
    }

    private static StructuralUnionModelPlan CreatePlan(string name, UnionNode union,
        IReadOnlyList<StructuralUnionArmPlan> arms) =>
        new()
        {
            Name = name,
            KindTypeName = $"{name}Kind",
            Namespace = GeneratedNamespace.Models,
            Description = union.Description,
            Arms = arms,
        };

    private static bool ValidateSpecialNumberArm(SchemaNode resolved, IReadOnlyList<JsonTokenType> tokens,
        string key, BindingErrorCollector errors)
    {
        if (resolved is not SpecialNumberNode || !tokens.Contains(JsonTokenType.String))
        {
            return true;
        }

        errors.Add(BindingErrorCategory.Schema, key,
            "a structural special-number arm requires an earlier text branch to own its named string spellings");
        return false;
    }

    private bool ValidateArmName(string armName, IReadOnlyList<StructuralUnionArmPlan> arms, string key,
        BindingErrorCollector errors)
    {
        if (armName is "Kind" or "Unknown")
        {
            errors.Add(BindingErrorCategory.Naming, key,
                $"structural union arm name '{armName}' collides with a reserved carrier member");
            return false;
        }

        if (!arms.Any(arm => _comparer.Equals(arm.Name, armName)))
        {
            return true;
        }

        errors.Add(BindingErrorCategory.Naming, key,
            $"multiple structural union branches map to arm name '{armName}'");
        return false;
    }

    private static SchemaNode? ResolveEffectiveBranch(SchemaNode original, SchemaNode resolved,
        IReadOnlyList<JsonTokenType> tokens, HashSet<JsonTokenType> claimedTokens, string key, int index,
        BindingErrorCollector errors)
    {
        var overlap = tokens.Where(claimedTokens.Contains).ToArray();
        if (overlap.Length is 0)
        {
            return original;
        }

        var remaining = tokens.Where(token => !claimedTokens.Contains(token)).ToArray();
        if (resolved is SpecialNumberNode
            && overlap is [JsonTokenType.String]
            && remaining is [JsonTokenType.Number])
        {
            // The earlier broad string branch owns named non-finite spellings. The numeric
            // remainder is an ordinary JSON number and must not write a named string itself.
            return new PrimitiveNode
            {
                Kind = PrimitiveKind.Number
            };
        }

        var tokenNames = string.Join(", ", overlap.Order().Select(static token => token.ToString()));
        errors.Add(BindingErrorCategory.Schema, key,
            $"structural union branch {index.ToString(System.Globalization.CultureInfo.InvariantCulture)} overlaps earlier branch token kind(s): {tokenNames}");
        return null;
    }

    private static SchemaNode Resolve(SchemaNode node, IReadOnlyDictionary<string, SchemaNode> graph, HashSet<string> visited)
    {
        if (node is not RefNode reference || !visited.Add(reference.Target)
                                          || !graph.TryGetValue(reference.Target, out var target))
        {
            return node;
        }

        return Resolve(target, graph, visited);
    }

    private static bool TryGetTokens(SchemaNode branch, out IReadOnlyList<JsonTokenType> tokens)
    {
        tokens = branch switch
        {
            PrimitiveNode { Kind: PrimitiveKind.String } or EnumNode or LiteralNode { Kind: LiteralKind.String }
                or JsonStringNode => [JsonTokenType.String],
            PrimitiveNode { Kind: PrimitiveKind.Number or PrimitiveKind.Integer }
                or LiteralNode { Kind: LiteralKind.Number } => [JsonTokenType.Number],
            PrimitiveNode { Kind: PrimitiveKind.Boolean } or LiteralNode { Kind: LiteralKind.Boolean } =>
                [JsonTokenType.True, JsonTokenType.False],
            SpecialNumberNode => [JsonTokenType.String, JsonTokenType.Number],
            ArrayNode or TupleNode => [JsonTokenType.StartArray],
            ObjectNode or DictionaryNode or FreeFormObjectNode
                or UnionNode { Classification: UnionClassification.Marked } => [JsonTokenType.StartObject],
            _ => [],
        };
        return tokens.Count > 0;
    }

    private static string ArmName(TypeReferencePlan type) => type switch
    {
        NamedTypeReferencePlan { Name: "string" } => "Text",
        NamedTypeReferencePlan { Name: "double" } or SpecialNumberTypeReferencePlan => "Number",
        NamedTypeReferencePlan { Name: "long" } => "Integer",
        NamedTypeReferencePlan { Name: "bool" } => "Boolean",
        NamedTypeReferencePlan named => CSharpNamePolicy.ToUnionConceptName(named.Name),
        ListTypeReferencePlan list => $"{ArmName(list.ElementType)}List",
        DictionaryTypeReferencePlan dictionary => $"{ArmName(dictionary.ValueType)}Dictionary",
        _ => throw new InvalidOperationException($"Unknown structural union arm type '{type.GetType().Name}'."),
    };
}
