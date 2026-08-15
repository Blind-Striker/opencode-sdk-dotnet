using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class ResponseAdapterEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(IReadOnlyList<ClientPlan> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        return Array.AsReadOnly(
        [
            .. clients
                .SelectMany(static client => client.Operations)
                .OrderBy(static operation => operation.Envelope.AdapterTypeName, StringComparer.Ordinal)
                .Select(static operation => EmitAdapter(operation)),
        ]);
    }

    private static GeneratedSource EmitAdapter(OperationPlan operation)
    {
        var envelope = operation.Envelope;
        var members = new List<MemberDeclarationSyntax>();
        members.AddRange(operation.ErrorMap.Statuses.Select(static status => EmitTagSet(status)));
        members.Add(SyntaxFactory.ConstructorDeclaration(envelope.AdapterTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
            .WithBody(SyntaxFactory.Block()));
        members.Add(EmitInstance(envelope.AdapterTypeName));
        members.Add(EmitAdapt(operation));
        if (envelope.Kind is EnvelopeKind.CursorList)
        {
            members.Add(EmitCursorSuccessHelper(envelope));
        }
        var declaration = SyntaxFactory.ClassDeclaration(envelope.AdapterTypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.InternalKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.Generic(
                    "ResponseAdapter",
                    TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName))))))
            .WithMembers(SyntaxFactory.List(members))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Adapts the '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' responses onto '{envelope.ResponseTypeName}'."));
        var unit = EmissionSyntax.CompilationUnit(
            "OpenCode.Sdk.Internal.ResponseAdapters",
            ["System", "OpenCode.Sdk.Internal.Serialization"],
            [declaration]);
        return EmissionSyntax.CreateSource($"Internal/ResponseAdapters/{envelope.AdapterTypeName}.cs", unit);
    }

    private static FieldDeclarationSyntax EmitTagSet(ErrorStatusPlan status)
    {
        var tags = status.Tags.Select(static tag => (ExpressionSyntax)SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(tag.Tag)));
        return SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(
                SyntaxFactory.ArrayType(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                    SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                        SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())))),
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(TagSetName(status.StatusCode))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.CollectionExpression(
                        SyntaxFactory.SeparatedList(tags.Select(SyntaxFactory.ExpressionElement)
                            .Cast<CollectionElementSyntax>())))))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));
    }

    private static PropertyDeclarationSyntax EmitInstance(string adapterTypeName) =>
        SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.EmitNamed(adapterTypeName), "Instance")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))))
            .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.ObjectCreationExpression(
                    TypeSyntaxEmitter.EmitNamed(adapterTypeName))
                .WithArgumentList(SyntaxFactory.ArgumentList())))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .WithLeadingTrivia(EmissionSyntax.Documentation("Gets the shared adapter instance."));

    private static MethodDeclarationSyntax EmitAdapt(OperationPlan operation)
    {
        var envelope = operation.Envelope;
        var arms = new List<SwitchExpressionArmSyntax>
        {
            SyntaxFactory.SwitchExpressionArm(
                SyntaxFactory.ConstantPattern(Number(envelope.SuccessStatusCode)),
                EmitSuccessCreation(envelope)),
            // Any other 2xx is outside the declared contract: a protocol failure, never an API error.
            SyntaxFactory.SwitchExpressionArm(
                SyntaxFactory.BinaryPattern(
                    SyntaxKind.AndPattern,
                    SyntaxFactory.RelationalPattern(SyntaxFactory.Token(SyntaxKind.GreaterThanEqualsToken), Number(200)),
                    SyntaxFactory.RelationalPattern(SyntaxFactory.Token(SyntaxKind.LessThanToken), Number(300))),
                SyntaxFactory.ThrowExpression(EmissionSyntax.Invocation(
                    SyntaxFactory.IdentifierName("UndeclaredSuccessFailure"),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("status"))))),
        };
        arms.AddRange(operation.ErrorMap.Statuses.Select(status => SyntaxFactory.SwitchExpressionArm(
            SyntaxFactory.ConstantPattern(Number(status.StatusCode)),
            EmitErrorCreation(envelope, SyntaxFactory.IdentifierName(TagSetName(status.StatusCode))))));
        arms.Add(SyntaxFactory.SwitchExpressionArm(
            SyntaxFactory.DiscardPattern(),
            EmitErrorCreation(envelope, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
        var statements = new List<StatementSyntax>();
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("rawBody"));
        statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.SwitchExpression(
            SyntaxFactory.IdentifierName("status"),
            SyntaxFactory.SeparatedList(arms))));
        return SyntaxFactory.MethodDeclaration(TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName), "Adapt")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("status"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("rawBody"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword))),
            ])))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(EmissionSyntax.Documentation("Maps one buffered response onto the typed envelope."));
    }

    /// <summary>
    /// Every success path is one deserialization pass: bare bodies read the payload type,
    /// wrapped bodies read their internal envelope DTO and project its members, and a
    /// no-content success refuses any body outright.
    /// </summary>
    private static ExpressionSyntax EmitSuccessCreation(EnvelopePlan envelope)
    {
        ExpressionSyntax Read()
        {
            var readTypeName = envelope.EnvelopeDtoTypeName
                               ?? envelope.PayloadTypeName
                               ?? throw new InvalidOperationException($"Envelope '{envelope.ResponseTypeName}' has no payload.");
            return EmissionSyntax.Invocation(
                SyntaxFactory.IdentifierName("ReadBarePayload"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("rawBody")),
                SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                    EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("OpenCodeJsonContext"), "Default"),
                    readTypeName)));
        }

        return envelope.Kind switch
        {
            EnvelopeKind.Bare => EmitSuccessInitializer(envelope, Read()),
            EnvelopeKind.Data => EmitSuccessInitializer(envelope, EmissionSyntax.MemberAccess(Read(), "Data")),
            EnvelopeKind.NoContent => EmitNoContentSuccess(envelope),
            EnvelopeKind.CursorList or _ => EmissionSyntax.Invocation(
                SyntaxFactory.IdentifierName("CreateSuccess"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("status")),
                SyntaxFactory.Argument(Read())),
        };
    }

    /// <summary>A declared no-content success must arrive bodiless; anything else is a protocol failure.</summary>
    private static ConditionalExpressionSyntax EmitNoContentSuccess(EnvelopePlan envelope) =>
        SyntaxFactory.ConditionalExpression(
            SyntaxFactory.IsPatternExpression(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("rawBody"), "Length"),
                SyntaxFactory.ConstantPattern(Number(0))),
            SyntaxFactory.ObjectCreationExpression(TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName))
                .WithInitializer(SyntaxFactory.InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    [
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName("Status"),
                            SyntaxFactory.IdentifierName("status")),
                    ]))),
            SyntaxFactory.ThrowExpression(EmissionSyntax.Invocation(
                SyntaxFactory.IdentifierName("NonEmptyNoContentFailure"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("status")))));

    private static ObjectCreationExpressionSyntax EmitSuccessInitializer(EnvelopePlan envelope, ExpressionSyntax payload) =>
        SyntaxFactory.ObjectCreationExpression(TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName))
            .WithInitializer(SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(
                [
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("Status"),
                        SyntaxFactory.IdentifierName("status")),
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(RequirePayloadName(envelope)),
                        payload),
                ])));

    private static string RequirePayloadName(EnvelopePlan envelope) =>
        envelope.PayloadName
        ?? throw new InvalidOperationException($"Envelope '{envelope.ResponseTypeName}' has no payload.");

    /// <summary>Cursor lists project the DTO twice, so a private helper keeps the switch arm single-read.</summary>
    private static MethodDeclarationSyntax EmitCursorSuccessHelper(EnvelopePlan envelope) =>
        SyntaxFactory.MethodDeclaration(TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName), "CreateSuccess")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("status"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("envelope"))
                    .WithType(TypeSyntaxEmitter.EmitNamed(envelope.EnvelopeDtoTypeName!)),
            ])))
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(
                SyntaxFactory.ObjectCreationExpression(TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName))
                    .WithInitializer(SyntaxFactory.InitializerExpression(
                        SyntaxKind.ObjectInitializerExpression,
                        SyntaxFactory.SeparatedList<ExpressionSyntax>(
                        [
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName("Status"),
                                SyntaxFactory.IdentifierName("status")),
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(RequirePayloadName(envelope)),
                                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("envelope"), "Data")),
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName("Cursor"),
                                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("envelope"), "Cursor")),
                        ])))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    private static ObjectCreationExpressionSyntax EmitErrorCreation(EnvelopePlan envelope, ExpressionSyntax allowedTags) =>
        SyntaxFactory.ObjectCreationExpression(TypeSyntaxEmitter.EmitNamed(envelope.ResponseTypeName))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("status")),
                SyntaxFactory.Argument(EmissionSyntax.Invocation(
                    SyntaxFactory.IdentifierName("ReadTolerantError"),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("rawBody")),
                    SyntaxFactory.Argument(allowedTags))),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName("rawBody")),
            ])));

    private static LiteralExpressionSyntax Number(int value) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(value));

    private static string TagSetName(int statusCode) =>
        $"Status{statusCode.ToString(CultureInfo.InvariantCulture)}Tags";
}
