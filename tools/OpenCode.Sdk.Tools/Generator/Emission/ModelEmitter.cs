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
        // union's variant carries both its own tag and the fixed outer tag.
        var chainMarkers = GetChainMarkerWireNames(implemented, unions);
        var unmatched = chainMarkers.Find(wireName =>
            model.Properties.Count(property => IsDiscriminator(property, wireName)) is not 1);
        if (unmatched is not null)
        {
            throw new InvalidOperationException($"Union variant '{model.Name}' must carry exactly one '{unmatched}' marker.");
        }

        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), model.Name)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
            [
                .. model.Properties.Select(property =>
                    EmitProperty(property, chainMarkers.Any(wireName => IsDiscriminator(property, wireName)))),
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

        var unit = EmissionSyntax.CompilationUnit(model.Namespace, CollectUsings(model), [declaration]);
        return EmissionSyntax.CreateSource($"Models/{model.Name}.cs", unit);
    }

    private static GeneratedSource EmitEnum(EnumModelPlan model)
    {
        // Strict by contract: the schema types enum values as strings, so the permissive
        // default's integer tolerance would admit malformed bodies as undefined members.
        var converterType = TypeSyntaxEmitter.Generic("StrictJsonStringEnumConverter", TypeSyntaxEmitter.EmitNamed(model.Name));
        var declaration = SyntaxFactory.EnumDeclaration(model.Name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonConverter", SyntaxFactory.AttributeArgument(
                SyntaxFactory.TypeOfExpression(converterType))))
            .WithMembers(SyntaxFactory.SeparatedList(model.Values.Select(static value => SyntaxFactory.EnumMemberDeclaration(value.Name)
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

    private static PropertyDeclarationSyntax EmitProperty(ModelPropertyPlan property, bool isDiscriminator)
    {
        var declaration = SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.Emit(property.Type), property.Name)
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(property.WireName)))
            .WithLeadingTrivia(EmissionSyntax.Documentation(property.Description ?? $"Gets the {DisplayName(property.Name)} value."));
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
                SyntaxFactory.AttributeArgument(EmissionSyntax.MemberAccess(
                        SyntaxFactory.IdentifierName("JsonIgnoreCondition"),
                        "WhenWritingNull"))
                    .WithNameEquals(SyntaxFactory.NameEquals("Condition"))));
        }

        if (isDiscriminator)
        {
            return declaration
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(EmitLiteral(property)))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        var modifiers = new List<SyntaxToken> { SyntaxFactory.Token(SyntaxKind.PublicKeyword), };
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
        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
        SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
    ]));

    private static LiteralExpressionSyntax EmitLiteral(ModelPropertyPlan property) => property.LiteralKind switch
    {
        LiteralKind.String => SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(property.LiteralValue ?? throw new InvalidOperationException("String literal had no value."))),
        LiteralKind.Boolean when bool.TryParse(property.LiteralValue, out var value) => SyntaxFactory.LiteralExpression(
            value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
        LiteralKind.Number => throw new InvalidOperationException($"Property '{property.Name}' uses a number literal, which has no emission consumer."),
        _ => throw new InvalidOperationException($"Property '{property.Name}' has an invalid literal plan."),
    };

    private static IReadOnlyList<string> CollectUsings(ObjectModelPlan model)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (model.Properties.Count > 0)
        {
            _ = result.Add("System.Text.Json.Serialization");
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

    private static bool IsDiscriminator(ModelPropertyPlan property, string markerWireName) =>
        property.IsLiteral && string.Equals(property.WireName, markerWireName, StringComparison.Ordinal);

    /// <summary>
    /// The marker each union in the schema's membership expects it to carry, walking every
    /// chain. Two unions may name different markers, and the schema then carries both; the
    /// same name is one property serving both contracts.
    /// </summary>
    private static List<string> GetChainMarkerWireNames(IReadOnlyList<UnionPlan> implemented,
        IReadOnlyDictionary<string, UnionPlan> unions)
    {
        var result = new List<string>();
        foreach (var union in implemented)
        {
            var current = union;
            while (current is not null)
            {
                if (!result.Contains(current.MarkerWireName, StringComparer.Ordinal))
                {
                    result.Add(current.MarkerWireName);
                }

                current = current.BaseTypeName is not null && unions.TryGetValue(current.BaseTypeName, out var outer)
                    ? outer
                    : null;
            }
        }

        return result;
    }

    private static string DisplayName(string name) =>
        string.Join(' ', CSharpNamePolicy.SplitWords(name).Select(static word => word.ToLowerInvariant()));
}
