using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>
/// Emits one public request record per query-carrying operation: derived records inherit the
/// <c>ListRequest</c> trio verbatim, flat records declare every bound query property.
/// </summary>
internal static class QueryRequestEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(IReadOnlyList<ClientPlan> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        return Array.AsReadOnly(
        [
            .. clients
                .SelectMany(static client => client.Operations)
                .Where(static operation => operation.QueryRequest is { RidesRequestBody: false })
                .OrderBy(static operation => operation.QueryRequest!.TypeName, StringComparer.Ordinal)
                .Select(static operation => EmitQueryRequest(operation)),
        ]);
    }

    private static GeneratedSource EmitQueryRequest(OperationPlan operation)
    {
        var queryRequest = operation.QueryRequest!;
        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), queryRequest.TypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
            [
                .. queryRequest.Properties
                    .Where(static property => !property.IsInherited)
                    .Select(static property => EmitProperty(property)),
            ]))
            .WithLeadingTrivia(EmissionSyntax.Documentation(RequestDocumentation(operation)));
        if (queryRequest.DerivesFromListRequest)
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed("ListRequest")))));
        }

        var unit = EmissionSyntax.CompilationUnit("OpenCode.Sdk", [], [declaration]);
        return EmissionSyntax.CreateSource($"{operation.RouteContainerName}/{queryRequest.TypeName}.cs", unit);
    }

    internal static PropertyDeclarationSyntax EmitProperty(QueryPropertyPlan property)
    {
        var accessors = SyntaxFactory.AccessorList(SyntaxFactory.List(
        [
            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
            SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
        ]));
        return SyntaxFactory.PropertyDeclaration(EmitPropertyType(property.Kind), property.PropertyName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(accessors)
            .WithLeadingTrivia(EmissionSyntax.Documentation(property.Description
                ?? $"Gets the '{property.WireName}' query value; the server default applies when unset."));
    }

    private static NullableTypeSyntax EmitPropertyType(QueryValueKind kind) => kind switch
    {
        QueryValueKind.Text => SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword))),
        QueryValueKind.ListOrder => SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("ListOrder")),
        QueryValueKind.BooleanText => SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("QueryBoolean")),
        QueryValueKind.SessionParentFilter => SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("SessionParentFilter")),
        QueryValueKind.Location => SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("LocationSelector")),
        _ => throw new InvalidOperationException($"Query value kind '{kind}' has no property type."),
    };

    private static string RequestDocumentation(OperationPlan operation)
    {
        var summary = $"Shapes the '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' query.";
        var inherited = operation.QueryRequest!.Properties
            .Where(static property => property.IsInherited && !string.IsNullOrWhiteSpace(property.Description))
            .Select(static property => $"'{property.WireName}': {property.Description}")
            .ToArray();
        return inherited.Length is 0 ? summary : $"{summary} {string.Join(' ', inherited)}";
    }
}
