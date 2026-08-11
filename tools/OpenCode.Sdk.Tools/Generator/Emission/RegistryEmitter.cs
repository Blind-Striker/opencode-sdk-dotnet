using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class RegistryEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(RegistryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var declaration = SyntaxFactory.ClassDeclaration("OpenCodeJsonContext")
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.InternalKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword),
                SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("JsonSerializerContext")))))
            .AddAttributeLists(EmissionSyntax.Attribute(
                "JsonSourceGenerationOptions",
                SyntaxFactory.AttributeArgument(EmissionSyntax.MemberAccess(
                        SyntaxFactory.IdentifierName("JsonSourceGenerationMode"),
                        "Metadata"))
                    .WithNameEquals(SyntaxFactory.NameEquals("GenerationMode"))));
        foreach (var typeName in plan.TypeNames.Order(StringComparer.Ordinal))
        {
            declaration = declaration.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonSerializable",
                SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression(TypeSyntaxEmitter.EmitNamed(typeName)))));
        }

        var unit = EmissionSyntax.CompilationUnit(
            "OpenCode.Sdk.Internal.Serialization",
            ["OpenCode.Sdk.Models", "System.Text.Json.Serialization"],
            [declaration]);
        return Array.AsReadOnly(
        [
            EmissionSyntax.CreateSource("Internal/Serialization/OpenCodeJsonContext.cs", unit),
        ]);
    }
}
