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
        var usings = operations.Any(static operation => operation.Parameters.Count > 0)
            ? new[] { "System" }
            : [];
        var unit = EmissionSyntax.CompilationUnit("OpenCode.Sdk", usings, [declaration]);
        return EmissionSyntax.CreateSource("OpenCodeRoutes.cs", unit);
    }

    private static ClassDeclarationSyntax EmitContainer(IGrouping<string, OperationPlan> container)
    {
        var members = new List<MemberDeclarationSyntax>();
        foreach (var operation in container.OrderBy(static operation => operation.RouteMemberName, StringComparer.Ordinal))
        {
            if (operation.Parameters.Count is 0)
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
            statements.AddRange(EmissionSyntax.ArgumentNullOrEmptyGuard(parameter.Name));
        }

        statements.Add(SyntaxFactory.ReturnStatement(EmitConcatenation(operation)));
        var documentation = EmissionSyntax.MemberDocumentation(
            $"Builds the '{operation.RouteTemplate}' route.",
            [.. operation.Parameters.Select(static parameter =>
                new DocumentedParameter(parameter.Name, $"The '{parameter.WireName}' route value."))],
            "The escaped route.");
        return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                operation.RouteMemberName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(operation.Parameters.Select(
                static parameter => SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                    .WithType(TypeSyntaxEmitter.EmitNamed(parameter.TypeName))))))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(documentation);
    }

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
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("Uri"), "EscapeDataString"),
                SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Name))));
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
