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
                .Where(static operation => operation.Envelope is not null)
                .OrderBy(static operation => operation.Envelope!.ResponseTypeName, StringComparer.Ordinal)
                .Select(static operation => EmitEnvelope(operation)),
        ]);
    }

    private static GeneratedSource EmitEnvelope(OperationPlan operation)
    {
        var envelope = operation.Envelope!;
        var members = envelope.Kind is EnvelopeKind.NoContent
            ? EmitNoContentMembers(envelope)
            : EmitPayloadMembers(envelope);
        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), envelope.ResponseTypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed("OpenCodeResponse")))))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List(members))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Represents the response of the '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' operation."));
        IReadOnlyList<string> usings = envelope.Kind switch
        {
            EnvelopeKind.NoContent => ["System.Diagnostics.CodeAnalysis", "OpenCode.Sdk.Models"],
            EnvelopeKind.CursorList or EnvelopeKind.DataLocationList =>
                ["System", "System.Diagnostics.CodeAnalysis", "System.Text", "OpenCode.Sdk.Models"],
            EnvelopeKind.Bare or EnvelopeKind.Data or EnvelopeKind.DataLocation or _ =>
                ["System", "System.Diagnostics.CodeAnalysis", "System.Text", "OpenCode.Sdk.Models"],
        };
        var unit = EmissionSyntax.CompilationUnit("OpenCode.Sdk", usings, [declaration]);
        return EmissionSyntax.CreateSource($"{operation.RouteContainerName}/{envelope.ResponseTypeName}.cs", unit);
    }

    private static List<MemberDeclarationSyntax> EmitNoContentMembers(EnvelopePlan envelope) =>
    [
        EmitSuccessConstructor(envelope.ResponseTypeName),
        EmitErrorConstructor(envelope),
    ];

    private static List<MemberDeclarationSyntax> EmitPayloadMembers(EnvelopePlan envelope)
    {
        var payloadName = RequirePayloadName(envelope);
        var fieldName = $"_{CSharpNamePolicy.ToCamelCase(payloadName)}";
        var payloadType = PayloadType(envelope);
        var members = new List<MemberDeclarationSyntax>
        {
            EmitBackingField(fieldName, payloadType),
        };
        if (envelope.Kind is EnvelopeKind.CursorList)
        {
            members.Add(EmitBackingField("_cursor", TypeSyntaxEmitter.EmitNamed("ListCursor")));
        }

        if (envelope.LocationTypeName is not null)
        {
            members.Add(EmitBackingField("_location", TypeSyntaxEmitter.EmitNamed(envelope.LocationTypeName)));
        }

        members.Add(EmitSuccessConstructor(envelope.ResponseTypeName));
        members.Add(EmitErrorConstructor(envelope));
        members.Add(EmitPayloadProperty(envelope, payloadType, fieldName));
        if (envelope.Kind is EnvelopeKind.CursorList)
        {
            members.Add(EmitCursorProperty());
        }

        if (envelope.LocationTypeName is not null)
        {
            members.Add(EmitLocationProperty(envelope.LocationTypeName));
        }

        members.Add(EmitPrintMembers(payloadName, fieldName));
        return members;
    }

    private static string RequirePayloadName(EnvelopePlan envelope) =>
        envelope.PayloadName
        ?? throw new InvalidOperationException($"Envelope '{envelope.ResponseTypeName}' has no payload.");

    private static TypeSyntax PayloadType(EnvelopePlan envelope)
    {
        var payloadTypeName = envelope.PayloadTypeName
                              ?? throw new InvalidOperationException($"Envelope '{envelope.ResponseTypeName}' has no payload.");
        return envelope.Kind is EnvelopeKind.CursorList or EnvelopeKind.DataLocationList
            ? TypeSyntaxEmitter.Generic("IReadOnlyList", TypeSyntaxEmitter.EmitNamed(payloadTypeName))
            : TypeSyntaxEmitter.EmitNamed(payloadTypeName);
    }

    private static FieldDeclarationSyntax EmitBackingField(string fieldName, TypeSyntax payloadType) =>
        SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(
                SyntaxFactory.NullableType(payloadType),
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

        var assignments = new List<StatementSyntax>
        {
            Assign("Status", SyntaxFactory.IdentifierName("status")),
            Assign("IsError", SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)),
            Assign("Error", SyntaxFactory.IdentifierName("error")),
            Assign("RawBody", SyntaxFactory.IdentifierName("rawBody")),
        };

        // The payload assignment is the SDK's single null-forgiveness: the guarded getter
        // makes the null unobservable. A no-content envelope has no payload to forgive.
        if (envelope.PayloadName is not null)
        {
            assignments.Add(Assign(envelope.PayloadName, SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
        }

        if (envelope.Kind is EnvelopeKind.CursorList)
        {
            assignments.Add(Assign("Cursor", SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
        }

        if (envelope.LocationTypeName is not null)
        {
            assignments.Add(Assign("Location", SyntaxFactory.PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
        }
        return SyntaxFactory.ConstructorDeclaration(responseTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword)))
            .AddAttributeLists(EmissionSyntax.Attribute("SetsRequiredMembers"))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("status"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("error"))
                    .WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("IOpenCodeError"))),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("rawBody"))
                    .WithType(SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))),
            ])))
            .WithBody(SyntaxFactory.Block(assignments))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                "Initializes an error-path instance; the payload stays unset behind its guard."));
    }

    private static PropertyDeclarationSyntax EmitPayloadProperty(EnvelopePlan envelope, TypeSyntax payloadType, string fieldName)
    {
        var payloadName = RequirePayloadName(envelope);
        return EmitGuardedProperty(
            payloadType,
            payloadName,
            fieldName,
            SyntaxFactory.IdentifierName("value"),
            $"Gets the {payloadName} payload; guarded on the error path.");
    }

    private static PropertyDeclarationSyntax EmitCursorProperty() =>
        EmitGuardedProperty(
            TypeSyntaxEmitter.EmitNamed("ListCursor"),
            "Cursor",
            "_cursor",
            SyntaxFactory.IdentifierName("value"),
            "Gets the page cursor; guarded on the error path.");

    private static PropertyDeclarationSyntax EmitLocationProperty(string locationTypeName) =>
        EmitGuardedProperty(
            TypeSyntaxEmitter.EmitNamed(locationTypeName),
            "Location",
            "_location",
            SyntaxFactory.IdentifierName("value"),
            "Gets the location the server resolved for the request; guarded on the error path.");

    private static PropertyDeclarationSyntax EmitGuardedProperty(TypeSyntax propertyType, string propertyName,
        string fieldName, ExpressionSyntax initValue, string documentation)
    {
        var guardMessage = $"The response is an error; check IsError before accessing {propertyName}.";
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
                initValue)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        return SyntaxFactory.PropertyDeclaration(propertyType, propertyName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.RequiredKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List([getter, setter])))
            .WithLeadingTrivia(EmissionSyntax.Documentation(documentation));
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
