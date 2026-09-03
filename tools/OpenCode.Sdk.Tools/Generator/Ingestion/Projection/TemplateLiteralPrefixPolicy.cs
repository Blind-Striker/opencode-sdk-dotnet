using System.Text;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

/// <summary>
/// Decodes exactly Effect's JSON-schema projection of <c>TemplateLiteral([literal, String])</c>:
/// <c>^</c> + <c>RegExp.escape(literal)</c> + <c>[\s\S]*?</c> + <c>$</c> (effect@4.0.0-rc.112,
/// <c>toJsonSchemaDocument.ts</c> / <c>SchemaAST.STRING_PATTERN</c> / <c>RegExp.escape</c>).
/// Every other pattern — number spans, union alternatives, nested spans, character classes,
/// validation prefixes — is not a prefix marker and stays ignored validation vocabulary.
/// </summary>
internal static class TemplateLiteralPrefixPolicy
{
    private const string StringSpan = "[\\s\\S]*?";

    /// <summary>The exact set <c>RegExp.escape</c> backslash-escapes; anything else appears unescaped in a literal.</summary>
    private const string Metacharacters = "/\\^$*+?.()|[]{}";

    public static string? TryDecodePrefix(string? pattern)
    {
        if (pattern is null || pattern.Length < StringSpan.Length + 3 || pattern[0] != '^' || pattern[^1] != '$')
        {
            return null;
        }

        var body = pattern.AsSpan(1, pattern.Length - 2);
        if (!body.EndsWith(StringSpan.AsSpan(), StringComparison.Ordinal))
        {
            return null;
        }

        var literal = body[..^StringSpan.Length];
        if (literal.IsEmpty)
        {
            return null;
        }

        var prefix = new StringBuilder(literal.Length);
        var index = 0;
        while (index < literal.Length)
        {
            var character = literal[index];
            if (character == '\\')
            {
                if (index + 1 >= literal.Length || !Metacharacters.Contains(literal[index + 1], StringComparison.Ordinal))
                {
                    return null;
                }

                _ = prefix.Append(literal[index + 1]);
                index += 2;
                continue;
            }

            if (Metacharacters.Contains(character, StringComparison.Ordinal))
            {
                return null;
            }

            _ = prefix.Append(character);
            index++;
        }

        return prefix.ToString();
    }
}
