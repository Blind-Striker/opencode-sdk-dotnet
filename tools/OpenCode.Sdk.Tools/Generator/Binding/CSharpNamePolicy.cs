using System.Text;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal static class CSharpNamePolicy
{
    public static string ToPascalCase(string wireName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);

        var words = SplitWords(wireName);
        var result = new StringBuilder(wireName.Length);
        foreach (var word in words)
        {
            AppendPascalWord(result, word);
        }

        if (result.Length is 0)
        {
            throw new ArgumentException("Wire name must contain at least one letter or digit.", nameof(wireName));
        }

        if (char.IsDigit(result[0]))
        {
            result.Insert(0, '_');
        }

        return result.ToString();
    }

    public static string ToCamelCase(string wireName)
    {
        var pascal = ToPascalCase(wireName);
        return string.Concat(char.ToLowerInvariant(pascal[0]), pascal[1..]);
    }

    public static bool IsValidIdentifier(string candidate)
    {
        if (string.IsNullOrEmpty(candidate) || !(char.IsLetter(candidate[0]) || candidate[0] is '_'))
        {
            return false;
        }

        // Reserved keywords would emit broken source; contextual keywords are legal identifiers.
        if (Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(candidate) is not Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
        {
            return false;
        }

        return candidate.Skip(1).All(static character => char.IsLetterOrDigit(character) || character is '_');
    }

    public static IReadOnlyList<string> SplitWords(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var words = new List<string>();
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (!char.IsLetterOrDigit(current))
            {
                AddWord(value, start, index, words);
                start = -1;
                continue;
            }

            if (start < 0)
            {
                start = index;
                continue;
            }

            var previous = value[index - 1];
            var nextIsLower = index + 1 < value.Length && char.IsLower(value[index + 1]);
            var upperBoundary = char.IsUpper(current)
                                && (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower));
            var digitBoundary = char.IsDigit(current) != char.IsDigit(previous)
                                && (char.IsDigit(current) || char.IsDigit(previous));
            var boundary = upperBoundary || digitBoundary;
            if (!boundary)
            {
                continue;
            }

            AddWord(value, start, index, words);
            start = index;
        }

        AddWord(value, start, value.Length, words);
        return Array.AsReadOnly([.. words]);
    }

    private static void AppendPascalWord(StringBuilder result, string word)
    {
        _ = result.Append(char.ToUpperInvariant(word[0]));
        if (word.Length > 1)
        {
            _ = result.Append(word[1..].ToLowerInvariant());
        }
    }

    private static void AddWord(string value, int start, int end, List<string> words)
    {
        if (start >= 0 && end > start)
        {
            words.Add(value[start..end]);
        }
    }
}
