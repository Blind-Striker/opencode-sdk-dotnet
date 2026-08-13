using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>Emits one virtual operation method delegating once into the pipeline.</summary>
internal static class OperationMethodEmitter
{
    public static MethodDeclarationSyntax Emit(OperationPlan operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var methodParameters = operation.Parameters
            .Where(static parameter => !parameter.IsHandleParameter)
            .ToArray();
        var statements = new List<StatementSyntax>();
        foreach (var parameter in methodParameters)
        {
            statements.AddRange(EmissionSyntax.ArgumentNullOrEmptyGuard(parameter.Name));
        }

        statements.Add(SyntaxFactory.ReturnStatement(EmitDelegation(operation)));
        return SyntaxFactory.MethodDeclaration(
                TypeSyntaxEmitter.Generic("Task", TypeSyntaxEmitter.EmitNamed(operation.Envelope.ResponseTypeName)),
                operation.MethodName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.VirtualKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(EmitParameters(methodParameters))))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(EmitDocumentation(operation, methodParameters));
    }

    private static IEnumerable<ParameterSyntax> EmitParameters(IReadOnlyList<OperationParameterPlan> methodParameters)
    {
        foreach (var parameter in methodParameters)
        {
            yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                .WithType(TypeSyntaxEmitter.EmitNamed(parameter.TypeName));
        }

        yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier("options"))
            .WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("OpenCodeRequestOptions")))
            .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier("cancellationToken"))
            .WithType(TypeSyntaxEmitter.EmitNamed("CancellationToken"))
            .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)));
    }

    private static InvocationExpressionSyntax EmitDelegation(OperationPlan operation) =>
        EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("Pipeline"), "ExecuteAsync"),
            SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                SyntaxFactory.IdentifierName("HttpMethod"),
                CSharpNamePolicy.ToPascalCase(operation.HttpMethod))),
            SyntaxFactory.Argument(EmitRoute(operation)),
            SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                SyntaxFactory.IdentifierName(operation.Envelope.AdapterTypeName),
                "Instance")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("options")),
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName("cancellationToken")));

    /// <summary>Route arguments come from the method for ordinary parameters and from the bound handle state otherwise.</summary>
    private static ExpressionSyntax EmitRoute(OperationPlan operation)
    {
        var member = EmissionSyntax.MemberAccess(
            EmissionSyntax.MemberAccess(
                SyntaxFactory.IdentifierName("OpenCodeRoutes"),
                operation.RouteContainerName),
            operation.RouteMemberName);
        if (operation.Parameters.Count is 0)
        {
            return member;
        }

        return EmissionSyntax.Invocation(member,
        [
            .. operation.Parameters.Select(static parameter => SyntaxFactory.Argument(parameter.IsHandleParameter
                ? SyntaxFactory.IdentifierName(CSharpNamePolicy.ToPascalCase(parameter.Name))
                : SyntaxFactory.IdentifierName(parameter.Name))),
        ]);
    }

    private static SyntaxTriviaList EmitDocumentation(OperationPlan operation, IReadOnlyList<OperationParameterPlan> methodParameters)
    {
        var parameters = new List<DocumentedParameter>();
        parameters.AddRange(methodParameters.Select(static parameter =>
            new DocumentedParameter(parameter.Name, $"The '{parameter.WireName}' route value.")));
        parameters.Add(new DocumentedParameter("options", "The per-call options."));
        parameters.Add(new DocumentedParameter("cancellationToken", "The cancellation token."));

        var exceptions = new List<DocumentedException>();
        if (operation.ErrorMap.Statuses.Count > 0)
        {
            var statuses = string.Join(
                ", ",
                operation.ErrorMap.Statuses.Select(static status => status.StatusCode.ToString(CultureInfo.InvariantCulture)));
            exceptions.Add(new DocumentedException(
                "OpenCodeApiException",
                $"The API returned an error status (declared: {statuses}) and NoThrow was not selected."));
        }
        else
        {
            exceptions.Add(new DocumentedException(
                "OpenCodeApiException",
                "The API returned an error status and NoThrow was not selected."));
        }

        exceptions.Add(new DocumentedException(
            "OpenCodeTransportException",
            "The server could not be reached or returned a malformed success body."));
        return EmissionSyntax.MemberDocumentation(
            operation.Summary ?? operation.Description ?? $"Calls the '{operation.RouteTemplate}' operation.",
            parameters,
            $"The '{operation.Envelope.ResponseTypeName}' envelope.",
            exceptions);
    }
}
