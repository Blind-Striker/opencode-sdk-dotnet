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
                ObjectModelPlan objectModel => EmitObject(objectModel, ResolveBaseUnion(objectModel, unions), unions),
                EnumModelPlan enumModel => EmitEnum(enumModel),
                _ => throw new InvalidOperationException($"Unknown model plan '{model.GetType().Name}'."),
            });
        }

        return Array.AsReadOnly([.. result]);
    }

    private static GeneratedSource EmitObject(ObjectModelPlan model, UnionPlan? baseUnion, IReadOnlyDictionary<string, UnionPlan> unions)
    {
        // A variant overrides one abstract marker per level of its union chain: a nested
        // union's variant carries both its own tag and the fixed outer tag.
        var chainMarkers = GetChainMarkerWireNames(baseUnion, unions);
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
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(model.Properties.Select(property =>
                EmitProperty(property, chainMarkers.Any(wireName => IsDiscriminator(property, wireName))))))
            .WithLeadingTrivia(EmissionSyntax.Documentation(model.Description ?? $"Represents a {DisplayName(model.Name)} value."));
        if (model.BaseTypeName is not null)
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                    SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed(model.BaseTypeName)))));
        }

        var unit = EmissionSyntax.CompilationUnit(model.Namespace, CollectUsings(model), [declaration]);
        return EmissionSyntax.CreateSource($"Models/{model.Name}.cs", unit);
    }

    private static GeneratedSource EmitEnum(EnumModelPlan model)
    {
        var converterType = TypeSyntaxEmitter.Generic("JsonStringEnumConverter", TypeSyntaxEmitter.EmitNamed(model.Name));
        var declaration = SyntaxFactory.EnumDeclaration(model.Name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonConverter", SyntaxFactory.AttributeArgument(
                SyntaxFactory.TypeOfExpression(converterType))))
            .WithMembers(SyntaxFactory.SeparatedList(model.Values.Select(static value => SyntaxFactory.EnumMemberDeclaration(value.Name)
                .AddAttributeLists(EmissionSyntax.Attribute("JsonStringEnumMemberName", EmissionSyntax.StringArgument(value.WireValue)))
                .WithLeadingTrivia(EmissionSyntax.Documentation($"Represents the '{value.WireValue}' wire value.")))))
            .WithLeadingTrivia(EmissionSyntax.Documentation(model.Description ?? $"Defines the supported {DisplayName(model.Name)} values."));
        var unit = EmissionSyntax.CompilationUnit(model.Namespace, ["System.Text.Json.Serialization"], [declaration]);
        return EmissionSyntax.CreateSource($"Models/{model.Name}.cs", unit);
    }

    private static PropertyDeclarationSyntax EmitProperty(ModelPropertyPlan property, bool isDiscriminator)
    {
        var declaration = SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.Emit(property.Type), property.Name)
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(property.WireName)))
            .WithLeadingTrivia(EmissionSyntax.Documentation(property.Description ?? $"Gets the {DisplayName(property.Name)} value."));
        if (property.Type.IsNullable)
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
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
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
            .WithAccessorList(property.Type.IsCollection
                ? EmitCollectionAccessors(property.Type, property.IsRequired)
                : EmitAutoAccessors());
        var initializer = EmitCollectionInitializer(property.Type);
        return initializer is null
            ? declaration
            : declaration.WithInitializer(SyntaxFactory.EqualsValueClause(initializer))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }

    private static AccessorListSyntax EmitAutoAccessors() => SyntaxFactory.AccessorList(SyntaxFactory.List(
    [
        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
        SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
    ]));

    private static AccessorListSyntax EmitCollectionAccessors(TypeReferencePlan type, bool isRequired)
    {
        var copy = EmitCollectionCopy(type, SyntaxFactory.IdentifierName("value"));
        AccessorDeclarationSyntax initAccessor;
        if (type.IsNullable)
        {
            initAccessor = EmitExpressionBodiedInit(SyntaxFactory.ConditionalExpression(
                SyntaxFactory.IsPatternExpression(
                    SyntaxFactory.IdentifierName("value"),
                    SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
                copy));
        }
        else if (!isRequired)
        {
            initAccessor = EmitExpressionBodiedInit(EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("OptionalCollectionInput"), "Normalize"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value")),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("field")),
                SyntaxFactory.Argument(Projection(
                    "input",
                    EmitCollectionCopy(type, SyntaxFactory.IdentifierName("input"))))));
        }
        else
        {
            var statements = new List<StatementSyntax>();
            statements.AddRange(EmissionSyntax.ArgumentNullGuard("value"));
            statements.Add(SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("field"),
                copy)));
            initAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                .WithBody(SyntaxFactory.Block(statements));
        }

        return SyntaxFactory.AccessorList(SyntaxFactory.List(
        [
            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
            initAccessor,
        ]));
    }

    private static AccessorDeclarationSyntax EmitExpressionBodiedInit(ExpressionSyntax value) =>
        SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("field"),
                value)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    private static ExpressionSyntax? EmitCollectionInitializer(TypeReferencePlan type)
    {
        if (!type.IsCollection || type.IsNullable)
        {
            return null;
        }

        return type switch
        {
            ListTypeReferencePlan list => EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(
                    SyntaxFactory.IdentifierName("Array"),
                    TypeSyntaxEmitter.Generic("Empty", TypeSyntaxEmitter.Emit(list.ElementType)))),
            DictionaryTypeReferencePlan dictionary => EmitEmptyDictionary(dictionary),
            _ => throw new InvalidOperationException($"Unknown collection plan '{type.GetType().Name}'."),
        };
    }

    private static ExpressionSyntax EmitCollectionCopy(TypeReferencePlan type, ExpressionSyntax value) => type switch
    {
        ListTypeReferencePlan list => EmitListCopy(list, value),
        DictionaryTypeReferencePlan dictionary => EmitDictionaryCopy(dictionary, value),
        _ => throw new InvalidOperationException($"Unknown collection plan '{type.GetType().Name}'."),
    };

    private static InvocationExpressionSyntax EmitListCopy(ListTypeReferencePlan list, ExpressionSyntax value)
    {
        var source = list.ElementType.IsCollection
            ? EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(value, "Select"),
                SyntaxFactory.Argument(Projection(
                    "element",
                    SyntaxFactory.CastExpression(
                        TypeSyntaxEmitter.Emit(list.ElementType),
                        EmitNestedCollectionCopy(list.ElementType, SyntaxFactory.IdentifierName("element"))))))
            : value;
        return EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(
                SyntaxFactory.ObjectCreationExpression(TypeSyntaxEmitter.Generic("List", TypeSyntaxEmitter.Emit(list.ElementType)))
                    .WithArgumentList(SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(source)))),
                "AsReadOnly"));
    }

    private static ObjectCreationExpressionSyntax EmitDictionaryCopy(DictionaryTypeReferencePlan dictionary, ExpressionSyntax value)
    {
        var pair = SyntaxFactory.IdentifierName("pair");
        var mutableCopy = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(value, "ToDictionary"),
            SyntaxFactory.Argument(Projection("pair", EmissionSyntax.MemberAccess(pair, "Key"))),
            SyntaxFactory.Argument(Projection(
                "pair",
                dictionary.ValueType.IsCollection
                    ? SyntaxFactory.CastExpression(
                        TypeSyntaxEmitter.Emit(dictionary.ValueType),
                        EmitNestedCollectionCopy(dictionary.ValueType, EmissionSyntax.MemberAccess(pair, "Value")))
                    : EmissionSyntax.MemberAccess(pair, "Value"))),
            SyntaxFactory.Argument(EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("StringComparer"), "Ordinal")));
        var readOnlyType = TypeSyntaxEmitter.Generic(
            "ReadOnlyDictionary",
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
            TypeSyntaxEmitter.Emit(dictionary.ValueType));
        return SyntaxFactory.ObjectCreationExpression(readOnlyType)
            .WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(mutableCopy))));
    }

    private static ExpressionSyntax EmitNestedCollectionCopy(TypeReferencePlan type, ExpressionSyntax value)
    {
        var copy = EmitCollectionCopy(type, value);
        return type.IsNullable
            ? SyntaxFactory.ConditionalExpression(
                SyntaxFactory.IsPatternExpression(
                    value,
                    SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
                copy)
            : copy;
    }

    private static SimpleLambdaExpressionSyntax Projection(string parameterName, ExpressionSyntax expression) =>
        SyntaxFactory.SimpleLambdaExpression(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName)),
                expression)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));

    private static ObjectCreationExpressionSyntax EmitEmptyDictionary(DictionaryTypeReferencePlan dictionary)
    {
        var dictionaryType = TypeSyntaxEmitter.Generic(
            "Dictionary",
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
            TypeSyntaxEmitter.Emit(dictionary.ValueType));
        var empty = SyntaxFactory.ObjectCreationExpression(dictionaryType)
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("StringComparer"), "Ordinal")))));
        var readOnlyType = TypeSyntaxEmitter.Generic(
            "ReadOnlyDictionary",
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
            TypeSyntaxEmitter.Emit(dictionary.ValueType));
        return SyntaxFactory.ObjectCreationExpression(readOnlyType)
            .WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(empty))));
    }

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
            if (property.Type is { IsCollection: true, IsNullable: false } && !property.IsRequired)
            {
                _ = result.Add("OpenCode.Sdk.Internal.Serialization");
            }

            CollectTypeUsings(property.Type, result);
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }

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
                _ = usings.Add("System");
                _ = usings.Add("System.Collections.Generic");
                if (list.ElementType.IsCollection)
                {
                    _ = usings.Add("System.Linq");
                }

                CollectTypeUsings(list.ElementType, usings);
                break;
            case DictionaryTypeReferencePlan dictionary:
                _ = usings.Add("System");
                _ = usings.Add("System.Collections.Generic");
                _ = usings.Add("System.Collections.ObjectModel");
                _ = usings.Add("System.Linq");
                CollectTypeUsings(dictionary.ValueType, usings);
                break;
        }
    }

    private static UnionPlan? ResolveBaseUnion(ObjectModelPlan model, Dictionary<string, UnionPlan> unions)
    {
        if (model.BaseTypeName is null)
        {
            return null;
        }

        return unions.TryGetValue(model.BaseTypeName, out var union)
            ? union
            : throw new InvalidOperationException($"Model '{model.Name}' references absent union '{model.BaseTypeName}'.");
    }

    private static bool IsDiscriminator(ModelPropertyPlan property, string markerWireName) =>
        property.IsLiteral && string.Equals(property.WireName, markerWireName, StringComparison.Ordinal);

    private static List<string> GetChainMarkerWireNames(UnionPlan? baseUnion, IReadOnlyDictionary<string, UnionPlan> unions)
    {
        var result = new List<string>();
        var current = baseUnion;
        while (current is not null)
        {
            result.Add(current.MarkerWireName);
            current = current.BaseTypeName is not null && unions.TryGetValue(current.BaseTypeName, out var outer) ? outer : null;
        }

        return result;
    }

    private static string DisplayName(string name) =>
        string.Join(' ', CSharpNamePolicy.SplitWords(name).Select(static word => word.ToLowerInvariant()));
}
