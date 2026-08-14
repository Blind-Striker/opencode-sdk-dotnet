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

        if (operation.RequestBody is { IsOptional: false } requiredBody)
        {
            statements.AddRange(EmissionSyntax.ArgumentNullGuard(requiredBody.ParameterName));
        }

        statements.Add(SyntaxFactory.ReturnStatement(EmitDelegation(operation)));
        return SyntaxFactory.MethodDeclaration(
                TypeSyntaxEmitter.Generic("Task", TypeSyntaxEmitter.EmitNamed(operation.Envelope.ResponseTypeName)),
                operation.MethodName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.VirtualKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(EmitParameters(operation, methodParameters))))
            .WithBody(SyntaxFactory.Block(statements))
            .WithLeadingTrivia(EmitDocumentation(operation, methodParameters));
    }

    private static IEnumerable<ParameterSyntax> EmitParameters(OperationPlan operation,
        IReadOnlyList<OperationParameterPlan> methodParameters)
    {
        foreach (var parameter in methodParameters)
        {
            yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameter.Name))
                .WithType(TypeSyntaxEmitter.EmitNamed(parameter.TypeName));
        }

        if (operation.RequestBody is not null)
        {
            var body = SyntaxFactory.Parameter(SyntaxFactory.Identifier(operation.RequestBody.ParameterName));
            yield return operation.RequestBody.IsOptional
                ? body.WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed(operation.RequestBody.TypeName)))
                    .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)))
                : body.WithType(TypeSyntaxEmitter.EmitNamed(operation.RequestBody.TypeName));
        }

        if (operation.Options is not null)
        {
            yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier("options"))
                .WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed(operation.Options.TypeName)))
                .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        }

        yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier("requestOptions"))
            .WithType(SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("OpenCodeRequestOptions")))
            .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        yield return SyntaxFactory.Parameter(SyntaxFactory.Identifier("cancellationToken"))
            .WithType(TypeSyntaxEmitter.EmitNamed("CancellationToken"))
            .WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)));
    }

    private static InvocationExpressionSyntax EmitDelegation(OperationPlan operation)
    {
        var arguments = new List<ArgumentSyntax>
        {
            SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                SyntaxFactory.IdentifierName("HttpMethod"),
                CSharpNamePolicy.ToPascalCase(operation.HttpMethod))),
            SyntaxFactory.Argument(EmitRoute(operation)),
        };
        if (operation.RequestBody is not null)
        {
            arguments.Add(SyntaxFactory.Argument(EmitBodyArgument(operation.RequestBody)));
            arguments.Add(SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
                EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("OpenCodeJsonContext"), "Default"),
                operation.RequestBody.TypeName)));
        }

        arguments.Add(SyntaxFactory.Argument(EmissionSyntax.MemberAccess(
            SyntaxFactory.IdentifierName(operation.Envelope.AdapterTypeName),
            "Instance")));
        arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("requestOptions")));
        arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("cancellationToken")));
        return EmissionSyntax.Invocation(
            EmissionSyntax.MemberAccess(SyntaxFactory.IdentifierName("Pipeline"), "ExecuteAsync"),
            [.. arguments]);
    }

    /// <summary>An omitted optional body still sends the empty JSON object the wire requires.</summary>
    private static ExpressionSyntax EmitBodyArgument(RequestBodyPlan body)
    {
        var parameter = SyntaxFactory.IdentifierName(body.ParameterName);
        if (!body.IsOptional)
        {
            return parameter;
        }

        return SyntaxFactory.BinaryExpression(
            SyntaxKind.CoalesceExpression,
            parameter,
            SyntaxFactory.ObjectCreationExpression(TypeSyntaxEmitter.EmitNamed(body.TypeName))
                .WithArgumentList(SyntaxFactory.ArgumentList()));
    }

    /// <summary>Route arguments come from the method for ordinary parameters and from the bound handle state otherwise.</summary>
    private static ExpressionSyntax EmitRoute(OperationPlan operation)
    {
        var member = EmissionSyntax.MemberAccess(
            EmissionSyntax.MemberAccess(
                SyntaxFactory.IdentifierName("OpenCodeRoutes"),
                operation.RouteContainerName),
            operation.RouteMemberName);
        if (operation.Parameters.Count is 0 && operation.Options is null)
        {
            return member;
        }

        var arguments = new List<ArgumentSyntax>();
        arguments.AddRange(operation.Parameters.Select(static parameter => SyntaxFactory.Argument(parameter.IsHandleParameter
            ? SyntaxFactory.IdentifierName(CSharpNamePolicy.ToPascalCase(parameter.Name))
            : SyntaxFactory.IdentifierName(parameter.Name))));
        if (operation.Options is not null)
        {
            arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("options")));
        }

        return EmissionSyntax.Invocation(member, [.. arguments]);
    }

    private static SyntaxTriviaList EmitDocumentation(OperationPlan operation, IReadOnlyList<OperationParameterPlan> methodParameters)
    {
        var parameters = new List<DocumentedParameter>();
        parameters.AddRange(methodParameters.Select(static parameter =>
            new DocumentedParameter(parameter.Name, $"The '{parameter.WireName}' route value.")));
        if (operation.RequestBody is not null)
        {
            parameters.Add(new DocumentedParameter(
                operation.RequestBody.ParameterName,
                operation.RequestBody.IsOptional
                    ? "The request body; an empty body is sent when omitted."
                    : "The request body."));
        }

        if (operation.Options is not null)
        {
            parameters.Add(new DocumentedParameter("options", "The query options."));
        }

        parameters.Add(new DocumentedParameter("requestOptions", "The per-call options."));
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
