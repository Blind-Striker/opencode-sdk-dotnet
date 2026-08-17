using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

internal static class TypeSyntaxEmitter
{
    public static TypeSyntax Emit(TypeReferencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var type = plan switch
        {
            NamedTypeReferencePlan named => EmitNamed(named.Name),
            SpecialNumberTypeReferencePlan => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword)),
            ListTypeReferencePlan list => Generic("IReadOnlyList", Emit(list.ElementType)),
            DictionaryTypeReferencePlan dictionary => Generic(
                "IReadOnlyDictionary",
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                Emit(dictionary.ValueType)),
            _ => throw new InvalidOperationException($"Unknown type-reference plan '{plan.GetType().Name}'."),
        };

        return plan.IsNullable ? SyntaxFactory.NullableType(type) : type;
    }

    public static TypeSyntax EmitMarker(LiteralKind kind) => kind switch
    {
        LiteralKind.String => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
        LiteralKind.Boolean => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
        LiteralKind.Number => throw new InvalidOperationException("Number markers have no emission consumer."),
        _ => throw new InvalidOperationException($"Unknown literal kind '{kind}'."),
    };

    public static TypeSyntax EmitNamed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name switch
        {
            "bool" => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
            "double" => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.DoubleKeyword)),
            "long" => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.LongKeyword)),
            "string" => SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
            _ => EmissionSyntax.QualifiedName(name),
        };
    }

    public static GenericNameSyntax Generic(string name, params TypeSyntax[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return SyntaxFactory.GenericName(SyntaxFactory.Identifier(name))
            .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(arguments)));
    }
}
