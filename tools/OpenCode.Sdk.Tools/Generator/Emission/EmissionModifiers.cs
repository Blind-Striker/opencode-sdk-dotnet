using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>
/// Single owner of the modifiers an emission mode puts on a generated client and its callable
/// members. A public family stays open to mocking through virtual members; an internal-raw
/// family is sealed shut because its hand-written door owns the public surface (ADR-0021).
/// </summary>
internal static class EmissionModifiers
{
    public static SyntaxTokenList Type(EmissionMode emission) => emission is EmissionMode.InternalRaw
        ? SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.InternalKeyword),
            SyntaxFactory.Token(SyntaxKind.SealedKeyword))
        : SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

    public static SyntaxTokenList Member(EmissionMode emission) => emission is EmissionMode.InternalRaw
        ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword))
        : SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.VirtualKeyword));
}
