using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class OptionalCollectionInputEmitter
{
    public static GeneratedSource Emit()
    {
        var typeParameter = SyntaxFactory.IdentifierName("T");
        var normalize = SyntaxFactory.MethodDeclaration(typeParameter, "Normalize")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.InternalKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithTypeParameterList(SyntaxFactory.TypeParameterList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.TypeParameter("T"))))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("value"))
                    .WithType(SyntaxFactory.NullableType(typeParameter)),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("empty"))
                    .WithType(typeParameter),
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("copy"))
                    .WithType(TypeSyntaxEmitter.Generic("Func", typeParameter, typeParameter)),
            ])))
            .WithConstraintClauses(SyntaxFactory.SingletonList(
                SyntaxFactory.TypeParameterConstraintClause(typeParameter)
                    .WithConstraints(SyntaxFactory.SingletonSeparatedList<TypeParameterConstraintSyntax>(
                        SyntaxFactory.ClassOrStructConstraint(SyntaxKind.ClassConstraint)))))
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ConditionalExpression(
                SyntaxFactory.IsPatternExpression(
                    SyntaxFactory.IdentifierName("value"),
                    SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))),
                SyntaxFactory.IdentifierName("empty"),
                EmissionSyntax.Invocation(
                    SyntaxFactory.IdentifierName("copy"),
                    SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value"))))))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        var declaration = SyntaxFactory.ClassDeclaration("OptionalCollectionInput")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.InternalKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(normalize));
        var unit = EmissionSyntax.CompilationUnit(
            "OpenCode.Sdk.Internal.Serialization",
            ["System"],
            [declaration]);
        return EmissionSyntax.CreateSource("Internal/Serialization/OptionalCollectionInput.cs", unit);
    }
}
