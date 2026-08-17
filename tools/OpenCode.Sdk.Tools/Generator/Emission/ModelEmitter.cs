using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class ModelEmitter
{
    private static readonly IReadOnlySet<string> PrimitiveValueTypeNames =
        new HashSet<string>(StringComparer.Ordinal) { "bool", "long", "double", };

    public static IReadOnlyList<GeneratedSource> Emit(EmitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var unions = plan.Unions.ToDictionary(static union => union.Name, StringComparer.Ordinal);

        // The wire-null-rejecting converter splits by class-versus-struct; generated enums are
        // the only value types beyond the primitive map.
        var valueTypeNames = new HashSet<string>(PrimitiveValueTypeNames, StringComparer.Ordinal);
        valueTypeNames.UnionWith(plan.Models.OfType<EnumModelPlan>().Select(static model => model.Name));

        var result = new List<GeneratedSource>(plan.Models.Count);
        foreach (var model in plan.Models.OrderBy(static model => model.Name, StringComparer.Ordinal))
        {
            result.Add(model switch
            {
                ObjectModelPlan objectModel => EmitObject(objectModel, ResolveImplementedUnions(objectModel, unions), unions, valueTypeNames),
                EnumModelPlan enumModel => EmitEnum(enumModel),
                _ => throw new InvalidOperationException($"Unknown model plan '{model.GetType().Name}'."),
            });
        }

        return Array.AsReadOnly([.. result]);
    }

    private static GeneratedSource EmitObject(ObjectModelPlan model, IReadOnlyList<UnionPlan> implemented, IReadOnlyDictionary<string, UnionPlan> unions,
        IReadOnlySet<string> valueTypeNames)
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
                    EmitProperty(property, chainMarkers.Any(wireName => IsDiscriminator(property, wireName)), valueTypeNames)),
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

    private static PropertyDeclarationSyntax EmitProperty(ModelPropertyPlan property, bool isDiscriminator,
        IReadOnlySet<string> valueTypeNames)
    {
        var declaration = SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.Emit(property.Type), property.Name)
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(property.WireName)))
            .WithLeadingTrivia(EmissionSyntax.Documentation(property.Description ?? $"Gets the {DisplayName(property.Name)} value."));
        if (ContainsSpecialNumber(property.Type) && !RejectsWireNull(property))
        {
            declaration = declaration.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonNumberHandling",
                SyntaxFactory.AttributeArgument(EmissionSyntax.MemberAccess(
                    SyntaxFactory.IdentifierName("JsonNumberHandling"),
                    "AllowNamedFloatingPointLiterals"))));
        }

        if (property.Type.IsNullable)
        {
            declaration = declaration.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonIgnore",
                SyntaxFactory.AttributeArgument(EmissionSyntax.MemberAccess(
                        SyntaxFactory.IdentifierName("JsonIgnoreCondition"),
                        "WhenWritingNull"))
                    .WithNameEquals(SyntaxFactory.NameEquals("Condition"))));
        }

        if (RejectsWireNull(property))
        {
            // The C# type stays nullable for absence; an explicit wire null is a contract
            // violation the converter turns into a JsonException.
            var converter = EmitWireNullRejectingConverter(property.Type, valueTypeNames);
            declaration = declaration.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonConverter",
                SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression(converter))));
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

            if (RejectsWireNull(property))
            {
                _ = result.Add("OpenCode.Sdk.Internal.Serialization");
            }

            CollectTypeUsings(property.Type, result);
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }

    /// <summary>An optional non-collection property whose schema does not admit null rejects an
    /// explicit wire null; required properties never reach the serializer as null-for-absence.</summary>
    private static bool RejectsWireNull(ModelPropertyPlan property) =>
        !property.IsRequired && !property.Type.IsCollection && !property.AllowsWireNull;

    private static TypeSyntax EmitWireNullRejectingConverter(TypeReferencePlan type, IReadOnlySet<string> valueTypeNames) => type switch
    {
        SpecialNumberTypeReferencePlan => SyntaxFactory.IdentifierName("WireNullRejectingSpecialNumberJsonConverter"),
        NamedTypeReferencePlan named => TypeSyntaxEmitter.Generic(
            valueTypeNames.Contains(named.Name) ? "WireNullRejectingValueJsonConverter" : "WireNullRejectingJsonConverter",
            TypeSyntaxEmitter.Emit(named with { IsNullable = false })),
        _ => throw new InvalidOperationException($"Type-reference plan '{type.GetType().Name}' cannot reject wire null."),
    };

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
