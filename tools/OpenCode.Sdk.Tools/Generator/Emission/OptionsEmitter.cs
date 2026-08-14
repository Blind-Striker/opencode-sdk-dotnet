using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>
/// Emits one public options record per query-carrying operation: derived records inherit the
/// <c>ListOptions</c> trio verbatim, flat records declare every bound query property.
/// </summary>
internal static class OptionsEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(IReadOnlyList<ClientPlan> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        return Array.AsReadOnly(
        [
            .. clients
                .SelectMany(static client => client.Operations)
                .Where(static operation => operation.Options is not null)
                .OrderBy(static operation => operation.Options!.TypeName, StringComparer.Ordinal)
                .Select(static operation => EmitOptions(operation)),
        ]);
    }

    private static GeneratedSource EmitOptions(OperationPlan operation)
    {
        var options = operation.Options!;
        var declaration = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), options.TypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
            [
                .. options.Properties
                    .Where(static property => !property.IsInherited)
                    .Select(static property => EmitProperty(property)),
            ]))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Shapes the '{operation.HttpMethod.ToUpperInvariant()} {operation.RouteTemplate}' query."));
        if (options.DerivesFromListOptions)
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(TypeSyntaxEmitter.EmitNamed("ListOptions")))));
        }

        var unit = EmissionSyntax.CompilationUnit("OpenCode.Sdk", [], [declaration]);
        return EmissionSyntax.CreateSource($"{options.TypeName}.cs", unit);
    }

    private static PropertyDeclarationSyntax EmitProperty(OptionsPropertyPlan property)
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
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Gets the '{property.WireName}' query value; the server default applies when unset."));
    }

    private static NullableTypeSyntax EmitPropertyType(QueryValueKind kind) => kind switch
    {
        QueryValueKind.Text => SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword))),
        QueryValueKind.PositiveCount => SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword))),
        QueryValueKind.ListOrder => SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("ListOrder")),
        QueryValueKind.SessionParentFilter => SyntaxFactory.NullableType(TypeSyntaxEmitter.EmitNamed("SessionParentFilter")),
        _ => throw new InvalidOperationException($"Query value kind '{kind}' has no property type."),
    };
}
