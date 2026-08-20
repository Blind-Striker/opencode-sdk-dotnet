using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class RoutesEmitter
{
    public static GeneratedSource Emit(IReadOnlyList<ClientPlan> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        var operations = clients.SelectMany(static client => client.Operations).ToArray();
        var containers = operations
            .GroupBy(static operation => operation.RouteContainerName, StringComparer.Ordinal)
            .OrderBy(static container => container.Key, StringComparer.Ordinal)
            .Select(EmitContainer)
            .ToArray();
        var declaration = SyntaxFactory.ClassDeclaration("OpenCodeRoutes")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(containers))
            .WithLeadingTrivia(EmissionSyntax.Documentation("Defines the wire route of every generated operation."));
        var usings = new List<string>();
        if (operations.Any(static operation => operation.Parameters.Count > 0))
        {
            usings.Add("System");
        }

        if (operations.Any(static operation => operation.QueryRequest is not null || operation.Parameters.Count > 0))
        {
            usings.Add("OpenCode.Sdk.Internal");
        }

        // A merged request types its route builder with the body model.
        if (operations.Any(static operation => operation.QueryRequest is { RidesRequestBody: true }))
        {
            usings.Add("OpenCode.Sdk.Models");
        }

        var unit = EmissionSyntax.CompilationUnit("OpenCode.Sdk", usings, [declaration]);
        return EmissionSyntax.CreateSource("OpenCodeRoutes.cs", unit);
    }

    private static ClassDeclarationSyntax EmitContainer(IGrouping<string, OperationPlan> container)
    {
        var members = new List<MemberDeclarationSyntax>();
        foreach (var operation in container.OrderBy(static operation => operation.RouteMemberName, StringComparer.Ordinal))
        {
            if (operation.Parameters.Count is 0 && operation.QueryRequest is null)
            {
                members.Add(EmitConst(
                    operation.RouteMemberName,
                    operation.RouteTemplate,
                    $"The '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' route."));
                continue;
            }

            members.Add(EmitConst(
                $"{operation.RouteMemberName}Template",
                operation.RouteTemplate,
                $"The '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' route template."));
            members.Add(EmitBuilder(operation));
        }

        return SyntaxFactory.ClassDeclaration(container.Key)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithMembers(SyntaxFactory.List(members))
            .WithLeadingTrivia(EmissionSyntax.Documentation($"Defines the '{container.Key}' routes."));
    }

    private static FieldDeclarationSyntax EmitConst(string name, string value, string documentation) =>
        SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name)
                    .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(value)))))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.ConstKeyword)))
            .WithLeadingTrivia(EmissionSyntax.Documentation(documentation));

    private static MethodDeclarationSyntax EmitBuilder(OperationPlan operation)
    {
        var statements = new List<StatementSyntax>();
        foreach (var parameter in operation.Parameters)
        {
            statements.AddRange(EmissionSyntax.RouteValueGuard(parameter.Name));
        }

        if (operation.QueryRequest is null)
        {
            statements.Add(SyntaxFactory.ReturnStatement(EmitConcatenation(operation)));
        }
        else
        {
            statements.AddRange(EmitQueryComposition(operation));
        }

        var parameters = new List<DocumentedParameter>();
        parameters.AddRange(operation.Parameters.Select(static parameter =>
            new DocumentedParameter(parameter.Name, $"The '{parameter.WireName}' route value.")));
        if (operation.QueryRequest is not null)
        {
            parameters.Add(new DocumentedParameter("request", "The request shaping the query."));
        }

        var documentation = EmissionSyntax.MemberDocumentation(
            $"Builds the '{operation.RouteTemplate}' route.",
            parameters,
            "The escaped route.");
        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                operation.RouteMemberName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(EmitBuilderParameters(operation))))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(documentation);
    }

    private static IEnumerable<ParameterSyntax> EmitBuilderParameters(OperationPlan operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                .WithType(TypeSyntaxEmitter.EmitNamed(parameter.TypeName));
        }

        if (operation.QueryRequest is not null)
        {
            yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier("request"))
                .WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed(operation.QueryRequest.TypeName)))
                .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        }
    }

    /// <summary>An unset request short-circuits to the bare path; a set one appends the composed query suffix.</summary>
    private static IEnumerable<StatementSyntax> EmitQueryComposition(OperationPlan operation)
    {
        yield return SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator("path")
                .WithInitializer(SyntaxFactory.EqualsValueClause(EmitConcatenation(operation))))));
        yield return SyntaxFactory.IfStatement(
            SyntaxFactory.IsPatternExpression(
                SyntaxFactory.IdentifierName("request"),
                SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
            SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("path"))));
        yield return SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator("query")
                .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.ObjectCreationExpression(
                        TypeSyntaxEmitter.EmitNamed("QueryStringBuilder"))
                    .WithArgumentList(SyntaxFactory.ArgumentList()))))));
        foreach (var property in operation.QueryRequest!.Properties)
        {
            var arguments = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(StringLiteral(property.WireName)),
                SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                    SyntaxFactory.IdentifierName("request"),
                    property.PropertyName)),
            };
            yield return SyntaxFactory.ExpressionStatement(EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("query"), QueryAddMethod(property.Kind)),
                [.. arguments]));
        }

        yield return SyntaxFactory.ReturnStatement(SyntaxFactory.BinaryExpression(
            SyntaxKind.AddExpression,
            SyntaxFactory.IdentifierName("path"),
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("query"), "Value")));
    }

    private static string QueryAddMethod(QueryValueKind kind) => kind switch
    {
        QueryValueKind.Text => "AddText",
        QueryValueKind.ListOrder => "AddOrder",
        QueryValueKind.BooleanText => "AddBoolean",
        QueryValueKind.SessionParentFilter => "AddParentFilter",
        QueryValueKind.Location => "AddLocation",
        _ => throw new InvalidOperationException($"Query value kind '{kind}' has no query-builder method."),
    };

    /// <summary>
    /// Guards one route value: null/empty/whitespace is refused, and so are the dot segments
    /// <c>Uri</c> canonicalization would silently rewrite into a different request path.
    /// </summary>
    /// <summary>Folds the template into literal + escaped-parameter concatenation, in template order.</summary>
    private static ExpressionSyntax EmitConcatenation(OperationPlan operation)
    {
        var pieces = new List<ExpressionSyntax>();
        var template = operation.RouteTemplate;
        var position = 0;
        foreach (var parameter in operation.Parameters)
        {
            var token = $"{{{parameter.WireName}}}";
            var start = template.IndexOf(token, position, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidOperationException(
                    $"Route template '{template}' does not contain parameter '{parameter.WireName}'.");
            }

            if (start > position)
            {
                pieces.Add(StringLiteral(template[position..start]));
            }

            pieces.Add(EmissionSyntax.Invocation(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("RouteValuePolicy"), "Escape"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Name)),
                SyntaxFactory.Argument(EmissionSyntax.Invocation(
                    SyntaxFactory.IdentifierName("nameof"),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Name))))));
            position = start + token.Length;
        }

        if (position < template.Length)
        {
            pieces.Add(StringLiteral(template[position..]));
        }

        return pieces.Aggregate(static (left, right) =>
            SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression, left, right));
    }

    private static LiteralExpressionSyntax StringLiteral(string value) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));
}
