using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class UnionEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(IReadOnlyList<UnionPlan> unions)
    {
        ArgumentNullException.ThrowIfNull(unions);
        var result = new List<GeneratedSource>(unions.Count * 3);
        foreach (var union in unions.OrderBy(static union => union.Name, StringComparer.Ordinal))
        {
            result.Add(EmitBase(union));
            result.Add(EmitUnknown(union));
            result.Add(EmitConverter(union));
        }

        return Array.AsReadOnly([.. result]);
    }

    private static GeneratedSource EmitBase(UnionPlan union)
    {
        var converterTypeName = $"{union.Name}JsonConverter";
        var marker = SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.EmitMarker(union.MarkerKind), union.MarkerName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.AbstractKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(union.MarkerWireName)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))))
            .WithLeadingTrivia(EmissionSyntax.Documentation($"Gets the '{union.MarkerWireName}' union marker."));
        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), union.Name)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.AbstractKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .AddAttributeLists(EmissionSyntax.Attribute(
                "JsonConverter",
                SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression(TypeSyntaxEmitter.EmitNamed(converterTypeName)))))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(marker))
            .WithLeadingTrivia(EmissionSyntax.Documentation(union.Description ?? $"Represents a {DisplayName(union.Name)} union."));
        if (union.BaseTypeName is not null)
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed(union.BaseTypeName)))));
        }

        var unit = EmissionSyntax.CompilationUnit(
            union.Namespace,
            ["OpenCode.Sdk.Internal.Serialization", "System.Text.Json.Serialization"],
            [declaration]);
        return EmissionSyntax.CreateSource($"Models/{union.Name}.cs", unit);
    }

    private static GeneratedSource EmitUnknown(UnionPlan union)
    {
        var markerType = TypeSyntaxEmitter.EmitMarker(union.MarkerKind);
        var members = new List<MemberDeclarationSyntax>
        {
            EmitUnknownMarkerField(markerType),
            EmitUnknownConstructor(union, markerType),
            EmitUnknownMarkerProperty(union, markerType),
        };
        if (union.FixedMarker is { } fixedMarker)
        {
            members.Add(EmitFixedMarkerProperty(fixedMarker));
        }

        members.Add(EmitUnknownPayloadProperty());
        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), union.UnknownTypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed(union.Name)))))
            .WithMembers(SyntaxFactory.List(members))
            .WithLeadingTrivia(EmissionSyntax.Documentation($"Preserves an unknown {DisplayName(union.Name)} payload."));
        var usingNames = union.MarkerKind is LiteralKind.String
            ? new[] { "System", "System.Text.Json", "System.Text.Json.Serialization", }
            : ["System.Text.Json", "System.Text.Json.Serialization"];
        var unit = EmissionSyntax.CompilationUnit(union.Namespace, usingNames, [declaration]);
        return EmissionSyntax.CreateSource($"Models/{union.UnknownTypeName}.cs", unit);
    }

    private static FieldDeclarationSyntax EmitUnknownMarkerField(TypeSyntax markerType) =>
        SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(markerType)
                .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator("_marker"))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

    private static ConstructorDeclarationSyntax EmitUnknownConstructor(UnionPlan union, TypeSyntax markerType)
    {
        var markerParameterName = CSharpNamePolicy.ToCamelCase(union.MarkerName);
        var statements = new List<StatementSyntax>();
        if (union.MarkerKind is LiteralKind.String)
        {
            statements.AddRange(EmissionSyntax.ArgumentNullOrEmptyGuard(markerParameterName));
        }

        statements.Add(SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("_marker"),
            SyntaxFactory.IdentifierName(markerParameterName))));
        statements.Add(SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("Payload"),
            EmissionSyntax.Invocation(EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("payload"), "Clone")))));
        return SyntaxFactory.ConstructorDeclaration(union.UnknownTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonConstructor"))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(markerParameterName)).WithType(markerType),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("payload")).WithType(SyntaxFactory.IdentifierName("JsonElement")),
            ])))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(EmissionSyntax.Documentation("Initializes an unknown union value from its marker and raw payload."));
    }

    private static PropertyDeclarationSyntax EmitUnknownMarkerProperty(UnionPlan union, TypeSyntax markerType) =>
        SyntaxFactory.PropertyDeclaration(markerType, union.MarkerName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(union.MarkerWireName)))
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.IdentifierName("_marker")))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .WithLeadingTrivia(EmissionSyntax.Documentation($"Gets the unrecognized '{union.MarkerWireName}' marker."));

    private static PropertyDeclarationSyntax EmitFixedMarkerProperty(UnionFixedMarkerPlan marker) =>
        SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.EmitMarker(marker.Kind), marker.Name)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("JsonPropertyName", EmissionSyntax.StringArgument(marker.WireName)))
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(EmitMarkerLiteral(marker.Kind, marker.Value)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .WithLeadingTrivia(EmissionSyntax.Documentation($"Gets the fixed '{marker.WireName}' marker of this nested union."));

    private static PropertyDeclarationSyntax EmitUnknownPayloadProperty() =>
        SyntaxFactory.PropertyDeclaration(SyntaxFactory.IdentifierName("JsonElement"), "Payload")
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))))
            .WithLeadingTrivia(EmissionSyntax.Documentation("Gets the preserved raw JSON payload."));

    private static GeneratedSource EmitConverter(UnionPlan union)
    {
        var converterName = $"{union.Name}JsonConverter";
        var declaration = SyntaxFactory.ClassDeclaration(converterName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.InternalKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.Generic("JsonConverter", TypeSyntaxEmitter.EmitNamed(union.Name))))))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
            [
                EmitDispatchMap(union),
                EmitHandleNull(),
                EmitRead(union),
                EmitWrite(union),
            ]));
        var unit = EmissionSyntax.CompilationUnit(
            "OpenCode.Sdk.Internal.Serialization",
            [
                "OpenCode.Sdk.Models",
                "System",
                "System.Collections.Generic",
                "System.Text.Json",
                "System.Text.Json.Serialization",
            ],
            [declaration]);
        return EmissionSyntax.CreateSource($"Internal/Serialization/{converterName}.cs", unit);
    }

    private static FieldDeclarationSyntax EmitDispatchMap(UnionPlan union)
    {
        var markerType = TypeSyntaxEmitter.EmitMarker(union.MarkerKind);
        var dictionaryType = TypeSyntaxEmitter.Generic("Dictionary", markerType, SyntaxFactory.IdentifierName("Type"));
        var entries = union.Variants.OrderBy(static variant => variant.Tag, StringComparer.Ordinal)
            .Select(variant => (ExpressionSyntax)SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.ImplicitElementAccess(SyntaxFactory.BracketedArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(EmitMarkerLiteral(union.MarkerKind, variant.Tag))))),
                SyntaxFactory.TypeOfExpression(TypeSyntaxEmitter.EmitNamed(variant.TypeName))))
            .ToArray();
        var initializer = SyntaxFactory.ObjectCreationExpression(dictionaryType)
            .WithArgumentList(union.MarkerKind is LiteralKind.String
                ? SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("StringComparer"), "Ordinal"))))
                : SyntaxFactory.ArgumentList())
            .WithInitializer(SyntaxFactory.InitializerExpression(
                SyntaxKind.CollectionInitializerExpression,
                SyntaxFactory.SeparatedList(entries)));
        return SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(dictionaryType)
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator("TypesByTag")
                        .WithInitializer(SyntaxFactory.EqualsValueClause(initializer)))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));
    }

    private static PropertyDeclarationSyntax EmitHandleNull() =>
        SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                "HandleNull")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    private static MethodDeclarationSyntax EmitRead(UnionPlan union)
    {
        var statements = new List<StatementSyntax>();
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("typeToConvert"));
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("options"));
        statements.AddRange(
        [
            SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
                    EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("reader"), "TokenType"),
                    EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonTokenType"), "Null")),
                ThrowJson($"The {union.Name} payload cannot be null.")),
            EmitPayloadDocument(),
            Local("payload", EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("document"), "RootElement")),
            EmitObjectPayloadCheck(union),
            EmitMarkerPresenceCheck(union),
        ]);
        statements.AddRange(EmitMarkerRead(union));
        statements.Add(EmitKnownDispatch(union));
        statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.IdentifierName(union.UnknownTypeName))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("marker")),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("payload")),
            ])))));

        return SyntaxFactory.MethodDeclaration(TypeSyntaxEmitter.EmitNamed(union.Name), "Read")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("reader"))
                    .WithType(SyntaxFactory.IdentifierName("Utf8JsonReader"))
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("typeToConvert")).WithType(SyntaxFactory.IdentifierName("Type")),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("options")).WithType(SyntaxFactory.IdentifierName("JsonSerializerOptions")),
            ])))
            .WithBody(SyntaxFactory.Block(statements));
    }

    private static LocalDeclarationStatementSyntax EmitPayloadDocument()
    {
        var parse = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonDocument"), "ParseValue"),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("reader"))
                .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)));
        return SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator("document").WithInitializer(SyntaxFactory.EqualsValueClause(parse)))))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword));
    }

    private static IfStatementSyntax EmitObjectPayloadCheck(UnionPlan union) =>
        SyntaxFactory.IfStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("payload"), "ValueKind"),
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonValueKind"), "Object")),
            ThrowJson($"The {union.Name} payload must be a JSON object."));

    private static IfStatementSyntax EmitMarkerPresenceCheck(UnionPlan union)
    {
        var tryGet = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("payload"), "TryGetProperty"),
            SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(union.MarkerWireName))),
            SyntaxFactory.Argument(SyntaxFactory.DeclarationExpression(
                    SyntaxFactory.IdentifierName("var"),
                    SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier("markerElement"))))
                .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword)));
        return SyntaxFactory.IfStatement(
            SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, tryGet),
            ThrowJson($"The {union.Name} payload must contain '{union.MarkerWireName}'."));
    }

    private static IReadOnlyList<StatementSyntax> EmitMarkerRead(UnionPlan union) => union.MarkerKind switch
    {
        LiteralKind.String => EmitStringMarkerRead(union),
        LiteralKind.Boolean => EmitBooleanMarkerRead(union),
        LiteralKind.Number => throw new InvalidOperationException($"Union '{union.Name}' uses a number marker, which has no emission consumer."),
        _ => throw new InvalidOperationException($"Unknown marker kind '{union.MarkerKind}'."),
    };

    private static IReadOnlyList<StatementSyntax> EmitStringMarkerRead(UnionPlan union) =>
    [
        SyntaxFactory.IfStatement(
            SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("markerElement"), "ValueKind"),
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonValueKind"), "String")),
            ThrowJson($"The '{union.MarkerWireName}' marker must be a string.")),
        Local("marker", EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("markerElement"), "GetString"))),
        SyntaxFactory.IfStatement(
            SyntaxFactory.IsPatternExpression(
                SyntaxFactory.IdentifierName("marker"),
                SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
            ThrowJson($"The '{union.MarkerWireName}' marker cannot be null.")),
    ];

    private static IReadOnlyList<StatementSyntax> EmitBooleanMarkerRead(UnionPlan union)
    {
        var valueKind = EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("markerElement"), "ValueKind");
        return
        [
            SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.NotEqualsExpression,
                        valueKind,
                        EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonValueKind"), "True")),
                    SyntaxFactory.BinaryExpression(
                        SyntaxKind.NotEqualsExpression,
                        valueKind,
                        EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonValueKind"), "False"))),
                ThrowJson($"The '{union.MarkerWireName}' marker must be a boolean.")),
            Local("marker", EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("markerElement"), "GetBoolean"))),
        ];
    }

    private static IfStatementSyntax EmitKnownDispatch(UnionPlan union)
    {
        var tryGet = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("TypesByTag"), "TryGetValue"),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("marker")),
            SyntaxFactory.Argument(SyntaxFactory.DeclarationExpression(
                    SyntaxFactory.IdentifierName("var"),
                    SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier("targetType"))))
                .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword)));
        var getTypeInfo = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("OpenCodeJsonContext"), "Default"),
                "GetTypeInfo"),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("targetType")));
        var typeInfo = SyntaxFactory.BinaryExpression(
            SyntaxKind.CoalesceExpression,
            getTypeInfo,
            SyntaxFactory.ThrowExpression(JsonException($"The generated context has no metadata for {union.Name}.")));
        var deserialize = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonSerializer"), "Deserialize"),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("payload")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("typeInfo")));
        var result = SyntaxFactory.BinaryExpression(
            SyntaxKind.CoalesceExpression,
            SyntaxFactory.BinaryExpression(
                SyntaxKind.AsExpression,
                deserialize,
                TypeSyntaxEmitter.EmitNamed(union.Name)),
            SyntaxFactory.ThrowExpression(JsonException($"The {union.Name} payload deserialized to null.")));
        return SyntaxFactory.IfStatement(tryGet, SyntaxFactory.Block(
            Local("typeInfo", typeInfo),
            SyntaxFactory.ReturnStatement(result)));
    }

    private static MethodDeclarationSyntax EmitWrite(UnionPlan union)
    {
        var unknownPattern = SyntaxFactory.IsPatternExpression(
            SyntaxFactory.IdentifierName("value"),
            SyntaxFactory.DeclarationPattern(
                SyntaxFactory.IdentifierName(union.UnknownTypeName),
                SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier("unknown"))));
        var writeUnknown = SyntaxFactory.Block(
            SyntaxFactory.ExpressionStatement(EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(
                    EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("unknown"), "Payload"),
                    "WriteTo"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("writer")))),
            SyntaxFactory.ReturnStatement());
        var getRuntimeType = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("value"), "GetType"));
        var getTypeInfo = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("OpenCodeJsonContext"), "Default"),
                "GetTypeInfo"),
            SyntaxFactory.Argument(getRuntimeType));
        var typeInfo = SyntaxFactory.BinaryExpression(
            SyntaxKind.CoalesceExpression,
            getTypeInfo,
            SyntaxFactory.ThrowExpression(JsonException($"The generated context has no metadata for {union.Name}.")));
        var serialize = EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonSerializer"), "Serialize"),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("writer")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("typeInfo")));
        var statements = new List<StatementSyntax>();
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("writer"));
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("value"));
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("options"));
        statements.Add(SyntaxFactory.IfStatement(unknownPattern, writeUnknown));

        // A nested-union variant must serialize through its declared base type so the nested
        // union's converter runs (its unknown carrier writes the raw payload); runtime-type
        // metadata would serialize the carrier as an ordinary record.
        foreach (var typeName in union.Variants.Where(static variant => variant.IsNestedUnion).Select(static variant => variant.TypeName))
        {
            var variableName = $"nested{typeName}";
            var nestedPattern = SyntaxFactory.IsPatternExpression(
                SyntaxFactory.IdentifierName("value"),
                SyntaxFactory.DeclarationPattern(
                    SyntaxFactory.IdentifierName(typeName),
                    SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(variableName))));
            var nestedSerialize = EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("JsonSerializer"), "Serialize"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("writer")),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(variableName)),
                SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                    EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("OpenCodeJsonContext"), "Default"),
                    typeName)));
            statements.Add(SyntaxFactory.IfStatement(
                nestedPattern,
                SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(nestedSerialize),
                    SyntaxFactory.ReturnStatement())));
        }

        statements.Add(Local("typeInfo", typeInfo));
        statements.Add(SyntaxFactory.ExpressionStatement(serialize));
        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                "Write")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("writer")).WithType(SyntaxFactory.IdentifierName("Utf8JsonWriter")),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("value")).WithType(TypeSyntaxEmitter.EmitNamed(union.Name)),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("options")).WithType(SyntaxFactory.IdentifierName("JsonSerializerOptions")),
            ])))
            .WithBody(SyntaxFactory.Block(statements));
    }

    private static LocalDeclarationStatementSyntax Local(string name, ExpressionSyntax value) =>
        SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(value)))));

    private static ThrowStatementSyntax ThrowJson(string message) => SyntaxFactory.ThrowStatement(JsonException(message));

    private static ObjectCreationExpressionSyntax JsonException(string message) =>
        SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("JsonException"))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(message))))));

    private static LiteralExpressionSyntax EmitMarkerLiteral(LiteralKind kind, string value) => kind switch
    {
        LiteralKind.String => SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(value)),
        LiteralKind.Boolean when bool.TryParse(value, out var boolean) => SyntaxFactory.LiteralExpression(
            boolean ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
        LiteralKind.Number => throw new InvalidOperationException("Number marker literals have no emission consumer."),
        _ => throw new InvalidOperationException($"Marker value '{value}' is invalid for '{kind}'."),
    };

    private static string DisplayName(string name) =>
        string.Join(' ', CSharpNamePolicy.SplitWords(name).Select(static word => word.ToLowerInvariant()));
}
