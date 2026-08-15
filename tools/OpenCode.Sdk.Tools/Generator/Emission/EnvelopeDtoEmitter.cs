using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>
/// Emits the internal single-pass deserialization DTO of every wrapped envelope; required
/// properties make a missing wire member a deserialization failure, so the adapters'
/// protocol walls hold without a second parse.
/// </summary>
internal static class EnvelopeDtoEmitter
{
    public static IReadOnlyList<GeneratedSource> Emit(IReadOnlyList<ClientPlan> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        return Array.AsReadOnly(
        [
            .. clients
                .SelectMany(static client => client.Operations)
                .Where(static operation => operation.Envelope.EnvelopeDtoTypeName is not null)
                .OrderBy(static operation => operation.Envelope.EnvelopeDtoTypeName, StringComparer.Ordinal)
                .Select(static operation => EmitDto(operation.Envelope)),
        ]);
    }

    private static GeneratedSource EmitDto(EnvelopePlan envelope)
    {
        var payloadTypeName = envelope.PayloadTypeName
                              ?? throw new InvalidOperationException($"Envelope '{envelope.ResponseTypeName}' has no payload.");
        var data = EmitProperty("data", "Data", EmitDataType(envelope.Kind, payloadTypeName));
        if (envelope.Kind is EnvelopeKind.CursorList)
        {
            // The page's element schema admits no null; the converter turns a null array or a
            // null element into a JsonException the adapters map to the transport wall.
            data = data.AddAttributeLists(EmissionSyntax.Attribute(
                "JsonConverter",
                SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression(TypeSyntaxEmitter.Generic(
                    "NullElementRejectingListJsonConverter",
                    TypeSyntaxEmitter.EmitNamed(payloadTypeName))))));
        }

        var members = new List<MemberDeclarationSyntax> { data, };
        if (envelope.Kind is EnvelopeKind.CursorList)
        {
            members.Add(EmitProperty("cursor", "Cursor", TypeSyntaxEmitter.EmitNamed("ListCursor")));
        }

        var declaration = SyntaxFactory.RecordDeclaration(
                SyntaxFactory.Token(SyntaxKind.RecordKeyword),
                envelope.EnvelopeDtoTypeName!)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.InternalKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithMembers(SyntaxFactory.List(members))
            .WithLeadingTrivia(EmissionSyntax.Documentation(
                $"Carries the wire body of '{envelope.ResponseTypeName}' through one deserialization pass."));
        var unit = EmissionSyntax.CompilationUnit(
            "OpenCode.Sdk.Internal.Serialization",
            ["System.Text.Json.Serialization", "OpenCode.Sdk.Models"],
            [declaration]);
        return EmissionSyntax.CreateSource($"Internal/Serialization/{envelope.EnvelopeDtoTypeName}.cs", unit);
    }

    private static TypeSyntax EmitDataType(EnvelopeKind kind, string payloadTypeName) => kind switch
    {
        EnvelopeKind.Data => TypeSyntaxEmitter.EmitNamed(payloadTypeName),
        EnvelopeKind.CursorList => TypeSyntaxEmitter.Generic("IReadOnlyList", TypeSyntaxEmitter.EmitNamed(payloadTypeName)),
        EnvelopeKind.Bare or EnvelopeKind.NoContent or _ => throw new InvalidOperationException($"Envelope kind '{kind}' has no DTO."),
    };

    private static PropertyDeclarationSyntax EmitProperty(string wireName, string name, TypeSyntax type) =>
        SyntaxFactory.PropertyDeclaration(type, name)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.RequiredKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(
            [
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
            ])))
            .AddAttributeLists(EmissionSyntax.Attribute(
                "JsonPropertyName",
                SyntaxFactory.AttributeArgument(SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(wireName)))));
}
