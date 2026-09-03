using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class ModelEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(EmitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var unions = plan.Unions.ToDictionary(static union => union.Name, StringComparer.Ordinal);

        var result = new List<GeneratedSource>(plan.Models.Count);
        foreach (var model in plan.Models.OrderBy(static model => model.Name, StringComparer.Ordinal))
        {
            if (model is StructuralUnionModelPlan)
            {
                continue;
            }

            result.Add(model switch
            {
                ObjectModelPlan objectModel => EmitObject(objectModel, ResolveImplementedUnions(objectModel, unions), unions),
                EnumModelPlan enumModel => EmitEnum(enumModel),
                _ => throw new InvalidOperationException($"Unknown model plan '{model.GetType().Name}'."),
            });
        }

        return Array.AsReadOnly([.. result]);
    }

    private static GeneratedSource EmitObject(ObjectModelPlan model, IReadOnlyList<UnionPlan> implemented,
        IReadOnlyDictionary<string, UnionPlan> unions)
    {
        // A variant overrides one abstract marker per level of its union chain: a nested
        // union's variant carries both its own tag and the fixed outer tag. A union's one
        // prefix-tagged arm carries that union's marker as a guarded string in place of a tag.
        var chainMarkers = GetChainMarkers(model.Name, implemented, unions);
        var unmatched = chainMarkers
            .FirstOrDefault(marker => model.Properties.Count(property => IsDiscriminator(property, marker)) is not 1);
        if (unmatched is not null)
        {
            throw new InvalidOperationException($"Union variant '{model.Name}' must carry exactly one '{unmatched.WireName}' marker.");
        }

        var declaration = SyntaxFactory
            .RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), model.Name)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
            [
                .. model.Properties.Select(property => EmitProperty(property, CarriedMarker(chainMarkers, property))),
                .. model.RequestQueryProperties.Select(static property => EmitRequestQueryProperty(property)),
            ]))
            .WithLeadingTrivia(EmissionSyntax.Documentation(model.Description ?? $"Represents a {DisplayName(model.Name)} value."));
        if (model.ImplementedUnionNames.Count > 0)
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SeparatedList<BaseTypeSyntax>(
            [
                .. model.ImplementedUnionNames.Select(static name =>
                    SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed(name))),
            ])));
        }

        var guardsPrefix = chainMarkers.Any(static marker => marker.Prefix is not null);
        var unit = EmissionSyntax.CompilationUnit(model.Namespace, CollectUsings(model, guardsPrefix), [declaration]);
        return EmissionSyntax.CreateSource($"Models/{model.Name}.cs", unit);
    }

    private static GeneratedSource EmitEnum(EnumModelPlan model)
    {
        // Strict by contract: the schema types enum values as strings, so the permissive
        // default's integer tolerance would admit malformed bodies as undefined members.
        var converterType = TypeSyntaxEmitter.Generic("StrictJsonStringEnumConverter", TypeSyntaxEmitter.EmitNamed(model.Name));
        var declaration = SyntaxFactory
            .EnumDeclaration(model.Name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonConverter", SyntaxFactory.AttributeArgument(
                SyntaxFactory.TypeOfExpression(converterType))))
            .WithMembers(SyntaxFactory.SeparatedList(model.Values.Select(static value => SyntaxFactory
                .EnumMemberDeclaration(value.Name)
                .AddAttributeLists(EmissionSyntax.Attribute("JsonStringEnumMemberName", EmissionSyntax.StringArgument(value.WireValue)))
                .WithLeadingTrivia(EmissionSyntax.Documentation($"Represents the '{value.WireValue}' wire value.")))))
            .WithLeadingTrivia(EmissionSyntax.Documentation(model.Description ?? $"Defines the supported {DisplayName(model.Name)} values."));
        var unit = EmissionSyntax.CompilationUnit(
            model.Namespace,
            ["System.Text.Json.Serialization", "OpenCode.Sdk.Internal.Serialization"],
            [declaration]);
        return EmissionSyntax.CreateSource($"Models/{model.Name}.cs", unit);
    }

    /// <summary>A merged request's query-side property never serializes; the route builder reads it.</summary>
    private static PropertyDeclarationSyntax EmitRequestQueryProperty(QueryPropertyPlan property)
    {
        // The documentation trivia must stay ahead of the added attribute list.
        var declaration = QueryRequestEmitter.EmitProperty(property);
        var documentation = declaration.GetLeadingTrivia();
        return declaration
            .WithoutLeadingTrivia()
            .AddAttributeLists(EmissionSyntax.Attribute("JsonIgnore"))
            .WithLeadingTrivia(documentation);
    }

    /// <summary>
    /// A discriminator is emitted under the marker member its union promises rather than under
    /// the property's own derived name, so a variant tagged by a second dialect's wire property
    /// still satisfies the one interface member. The wire name it serializes as is untouched.
    /// A literal tag is a constant; the prefix-tagged arm's marker is a required string whose
    /// initializer refuses any value outside the prefix.
    /// </summary>
    private static PropertyDeclarationSyntax EmitProperty(ModelPropertyPlan property, ChainMarker? marker)
    {
        var memberName = marker?.MemberName ?? property.Name;
        var declaration = SyntaxFactory
            .PropertyDeclaration(TypeSyntaxEmitter.Emit(property.Type), memberName)
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(property.WireName)))
            .WithLeadingTrivia(EmissionSyntax.Documentation(property.Description ?? $"Gets the {DisplayName(memberName)} value."));
        if (ContainsSpecialNumber(property.Type))
        {
            declaration = declaration.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonNumberHandling",
                SyntaxFactory.AttributeArgument(EmissionSyntax.MemberAccess(
                    SyntaxFactory.IdentifierName("JsonNumberHandling"),
                    "AllowNamedFloatingPointLiterals"))));
        }

        if (!property.IsRequired)
        {
            declaration = declaration.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonIgnore",
                SyntaxFactory
                    .AttributeArgument(EmissionSyntax.MemberAccess(
                        SyntaxFactory.IdentifierName("JsonIgnoreCondition"),
                        "WhenWritingNull"))
                    .WithNameEquals(SyntaxFactory.NameEquals("Condition"))));
        }

        if (marker?.Prefix is { } prefix)
        {
            if (!property.IsRequired)
            {
                throw new InvalidOperationException($"Property '{property.Name}' carries a prefix-tagged marker but is not required.");
            }

            return declaration
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.RequiredKeyword)))
                .WithAccessorList(EmitPrefixGuardedAccessors(property.WireName, prefix));
        }

        if (marker is not null)
        {
            return declaration
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(EmitLiteral(property)))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        var modifiers = new List<SyntaxToken>
        {
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
        };
        if (property.IsRequired)
        {
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.RequiredKeyword));
        }

        declaration = declaration
            .WithModifiers(SyntaxFactory.TokenList(modifiers))
            .WithAccessorList(EmitAutoAccessors());
        return declaration;
    }

    private static AccessorListSyntax EmitAutoAccessors() => SyntaxFactory.AccessorList(SyntaxFactory.List(
    [
        SyntaxFactory
            .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
        SyntaxFactory
            .AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
    ]));

    /// <summary>
    /// <c>get; init { … }</c> for the prefix-tagged arm's marker. The initializer refuses null
    /// and any value outside the prefix — the typed twin of the unknown carrier refusing a
    /// marker the prefix claims — and stores through the <c>field</c> keyword.
    /// </summary>
    private static AccessorListSyntax EmitPrefixGuardedAccessors(string wireName, string prefix)
    {
        var refusal = SyntaxFactory.IfStatement(
            SyntaxFactory.PrefixUnaryExpression(
                SyntaxKind.LogicalNotExpression,
                EmissionSyntax.StartsWithOrdinal(SyntaxFactory.IdentifierName("value"), prefix)),
            SyntaxFactory.Block(EmissionSyntax.ThrowArgumentException(
                $"The '{wireName}' marker must carry the '{prefix}' prefix.",
                "value")));
        var store = SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.FieldExpression(),
            SyntaxFactory.IdentifierName("value")));
        return SyntaxFactory.AccessorList(SyntaxFactory.List(
        [
            SyntaxFactory
                .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
            SyntaxFactory
                .AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                .WithBody(SyntaxFactory.Block(SyntaxFactory.List(
                [
                    .. EmissionSyntax.ArgumentNullGuard("value"),
                    refusal,
                    store,
                ]))),
        ]));
    }

    private static LiteralExpressionSyntax EmitLiteral(ModelPropertyPlan property) => property.LiteralKind switch
    {
        LiteralKind.String => SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(property.LiteralValue ?? throw new InvalidOperationException("String literal had no value."))),
        LiteralKind.Boolean when bool.TryParse(property.LiteralValue, out var value) => SyntaxFactory.LiteralExpression(
            value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
        LiteralKind.Number => throw new InvalidOperationException(
            $"Property '{property.Name}' uses a number literal, which has no emission consumer."),
        _ => throw new InvalidOperationException($"Property '{property.Name}' has an invalid literal plan."),
    };

    private static IReadOnlyList<string> CollectUsings(ObjectModelPlan model, bool guardsPrefix)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (model.Properties.Count > 0)
        {
            _ = result.Add("System.Text.Json.Serialization");
        }

        // The guarded marker's initializer throws BCL argument exceptions and compares ordinally.
        if (guardsPrefix)
        {
            _ = result.Add("System");
        }

        foreach (var property in model.Properties)
        {
            CollectTypeUsings(property.Type, result);
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }

    private static bool ContainsSpecialNumber(TypeReferencePlan type) => type switch
    {
        SpecialNumberTypeReferencePlan => true,
        ListTypeReferencePlan list => ContainsSpecialNumber(list.ElementType),
        DictionaryTypeReferencePlan dictionary => ContainsSpecialNumber(dictionary.ValueType),
        _ => false,
    };

    private static void CollectTypeUsings(TypeReferencePlan type, ISet<string> usings)
    {
        switch (type)
        {
            case NamedTypeReferencePlan { Name: "Uri" }:
                _ = usings.Add("System");
                break;
            case NamedTypeReferencePlan { Name: "JsonElement" }:
                _ = usings.Add("System.Text.Json");
                break;
            case BinaryTypeReferencePlan:
                _ = usings.Add("System");
                break;
            case ListTypeReferencePlan list:
                _ = usings.Add("System.Collections.Generic");
                CollectTypeUsings(list.ElementType, usings);
                break;
            case DictionaryTypeReferencePlan dictionary:
                _ = usings.Add("System.Collections.Generic");
                CollectTypeUsings(dictionary.ValueType, usings);
                break;
        }
    }

    private static List<UnionPlan> ResolveImplementedUnions(ObjectModelPlan model, Dictionary<string, UnionPlan> unions) =>
    [
        .. model.ImplementedUnionNames.Select(name => unions.TryGetValue(name, out var union)
            ? union
            : throw new InvalidOperationException($"Model '{model.Name}' references absent union '{name}'.")),
    ];

    /// <summary>
    /// A variant carries a chain marker as the literal its tag fixes or, as the union's one
    /// prefix-tagged arm, as the plain string the prefix claims — never as both.
    /// </summary>
    private static bool IsDiscriminator(ModelPropertyPlan property, ChainMarker marker) =>
        string.Equals(property.WireName, marker.WireName, StringComparison.Ordinal)
        && (marker.Prefix is null ? property.IsLiteral : !property.IsLiteral);

    private static ChainMarker? CarriedMarker(IEnumerable<ChainMarker> markers, ModelPropertyPlan property) =>
        markers.FirstOrDefault(marker => IsDiscriminator(property, marker));

    /// <summary>
    /// The marker each union in the schema's membership expects it to carry, walking every
    /// chain, paired with the member name that union promises for it and, when the schema is
    /// that union's prefix-tagged arm, the prefix its value must carry. Two unions may name
    /// different markers, and the schema then carries both; the same name is one property
    /// serving both contracts. A union that dispatches on more than one wire property reads
    /// this schema's own variant entry, so each variant answers under the union's one member.
    /// </summary>
    private static List<ChainMarker> GetChainMarkers(string modelName,
        IReadOnlyList<UnionPlan> implemented, IReadOnlyDictionary<string, UnionPlan> unions)
    {
        var result = new List<ChainMarker>();
        foreach (var union in implemented)
        {
            var current = union;
            while (current is not null)
            {
                var wireName = VariantMarkerWireName(current, modelName);
                if (!result.Any(entry => string.Equals(entry.WireName, wireName, StringComparison.Ordinal)))
                {
                    result.Add(new ChainMarker(wireName, current.MarkerName, PrefixOf(current, modelName, wireName)));
                }

                current = current.BaseTypeName is not null && unions.TryGetValue(current.BaseTypeName, out var outer)
                    ? outer
                    : null;
            }
        }

        return result;
    }

    private static string VariantMarkerWireName(UnionPlan union, string modelName) =>
        union
            .Variants.FirstOrDefault(variant => string.Equals(variant.TypeName, modelName, StringComparison.Ordinal))
            ?.MarkerWireName
        ?? union.MarkerWireName;

    /// <summary>The prefix a union's prefix-tagged arm must carry on its marker; null for every other variant.</summary>
    private static string? PrefixOf(UnionPlan union, string modelName, string wireName) =>
        union.PrefixVariant is { } prefix
        && string.Equals(prefix.TypeName, modelName, StringComparison.Ordinal)
        && string.Equals(prefix.MarkerWireName, wireName, StringComparison.Ordinal)
            ? prefix.Prefix
            : null;

    private static string DisplayName(string name) =>
        string.Join(' ', CSharpNamePolicy.SplitWords(name).Select(static word => word.ToLowerInvariant()));

    /// <summary>
    /// One marker a union in the schema's chain expects it to carry: the wire property, the
    /// member the union promises for it, and — for the union's prefix-tagged arm — the prefix
    /// the member's value must carry in place of a literal tag.
    /// </summary>
    private sealed record ChainMarker(string WireName, string MemberName, string? Prefix);
}
