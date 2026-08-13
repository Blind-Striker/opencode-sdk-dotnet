using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class EnvelopeEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(IReadOnlyList<ClientPlan> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        return Array.AsReadOnly(
        [
            .. clients
                .SelectMany(static client => client.Operations)
                .OrderBy(static operation => operation.Envelope.ResponseTypeName, StringComparer.Ordinal)
                .Select(static operation => EmitEnvelope(operation)),
        ]);
    }

    private static GeneratedSource EmitEnvelope(OperationPlan operation)
    {
        var envelope = operation.Envelope;
        var fieldName = $"_{CSharpNamePolicy.ToCamelCase(envelope.PayloadName)}";
        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), envelope.ResponseTypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed("OpenCodeResponse")))))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
            [
                EmitBackingField(fieldName, envelope.PayloadTypeName),
                EmitSuccessConstructor(envelope.ResponseTypeName),
                EmitErrorConstructor(envelope),
                EmitPayloadProperty(envelope, fieldName),
                EmitPrintMembers(envelope.PayloadName, fieldName),
            ]))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Represents the response of the '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' operation."));
        var unit = EmissionSyntax.CompilationUnit(
            "OpenCode.Sdk",
            ["System", "System.Diagnostics.CodeAnalysis", "System.Text", "OpenCode.Sdk.Models"],
            [declaration]);
        return EmissionSyntax.CreateSource($"{envelope.ResponseTypeName}.cs", unit);
    }

    private static FieldDeclarationSyntax EmitBackingField(string fieldName, string payloadTypeName) =>
        SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(
                SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed(payloadTypeName)),
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(fieldName))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

    private static ConstructorDeclarationSyntax EmitSuccessConstructor(string responseTypeName) =>
        SyntaxFactory.ConstructorDeclaration(responseTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithBody(SyntaxFactory.Block())
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Initializes a success instance of the '{responseTypeName}' envelope."));

    private static ConstructorDeclarationSyntax EmitErrorConstructor(EnvelopePlan envelope)
    {
        var responseTypeName = envelope.ResponseTypeName;

        // The payload assignment is the SDK's single null-forgiveness: the guarded getter
        // makes the null unobservable.
        var assignments = new StatementSyntax[]
        {
            Assign("Status", SyntaxFactory.IdentifierName("status")),
            Assign("IsError", SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)),
            Assign("Error", SyntaxFactory.IdentifierName("error")),
            Assign("RawBody", SyntaxFactory.IdentifierName("rawBody")),
            Assign(envelope.PayloadName, SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
        };
        return SyntaxFactory.ConstructorDeclaration(responseTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("SetsRequiredMembers"))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("status"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("error"))
                    .WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("OpenCodeError"))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("rawBody"))
                    .WithType(SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))),
            ])))
            .WithBody(SyntaxFactory.Block(assignments))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                "Initializes an error-path instance; the payload stays unset behind its guard."));
    }

    private static PropertyDeclarationSyntax EmitPayloadProperty(EnvelopePlan envelope, string fieldName)
    {
        var guardMessage = $"The response is an error; check IsError before accessing {envelope.PayloadName}.";
        var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.BinaryExpression(
                SyntaxKind.CoalesceExpression,
                SyntaxFactory.IdentifierName(fieldName),
                SyntaxFactory.ThrowExpression(SyntaxFactory.ObjectCreationExpression(
                        TypeSyntaxEmitter.EmitNamed("InvalidOperationException"))
                    .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(guardMessage))))))))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        var setter = SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(fieldName),
                SyntaxFactory.IdentifierName("value"))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        return SyntaxFactory.PropertyDeclaration(TypeSyntaxEmitter.EmitNamed(envelope.PayloadTypeName), envelope.PayloadName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.RequiredKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List([getter, setter])))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Gets the {envelope.PayloadName} payload; guarded on the error path."));
    }

    private static MethodDeclarationSyntax EmitPrintMembers(string payloadName, string fieldName)
    {
        var payloadField = SyntaxFactory.IdentifierName(fieldName);
        var builder = SyntaxFactory.IdentifierName("builder");
        var statements = new List<StatementSyntax>();
        statements.AddRange(EmissionSyntax.ArgumentNullGuard("builder"));
        statements.Add(SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator("printed")
                .WithInitializer(SyntaxFactory.EqualsValueClause(EmissionSyntax.Invocation(
                    EmissionSyntax.MemberAccess(SyntaxFactory.BaseExpression(), "PrintMembers"),
                    SyntaxFactory.Argument(builder))))))));
        statements.Add(SyntaxFactory.IfStatement(
            SyntaxFactory.IsPatternExpression(
                payloadField,
                SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
            SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("printed")))));
        statements.Add(SyntaxFactory.IfStatement(
            SyntaxFactory.IdentifierName("printed"),
            SyntaxFactory.Block(Discard(EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(builder, "Append"),
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(", "))))))));
        statements.Add(Discard(EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(
                EmissionSyntax.Invocation(
                    EmissionSyntax.MemberAccess(builder, "Append"),
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal($"{payloadName} = ")))),
                "Append"),
            SyntaxFactory.Argument(payloadField))));
        statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                "PrintMembers")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.ProtectedKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("builder"))
                    .WithType(TypeSyntaxEmitter.EmitNamed("StringBuilder")))))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                "Prints the shared metadata and appends the payload only when it is present."));
    }

    private static ExpressionStatementSyntax Assign(string target, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName(target),
            value));

    private static ExpressionStatementSyntax Discard(ExpressionSyntax expression) =>
        SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("_"),
            expression));
}
