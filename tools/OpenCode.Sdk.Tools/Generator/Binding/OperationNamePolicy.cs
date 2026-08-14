using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Derives C# member and type names for one bound operation from its identifier segments.
/// Verb detection is structural — only the final segment can be a verb — so mid-position
/// segments that spell a verb stay in the subject. Derivations that need the pluralized
/// group return <see langword="null"/> when the naive rule is unsafe; the binder refuses
/// such operations until the rule set grows.
/// </summary>
internal static class OperationNamePolicy
{
    /// <summary>Identifier segments recognized as operation verbs when they close the identifier.</summary>
    private static readonly string[] KnownVerbSegments = ["create", "get", "list"];

    /// <summary>Gets the operation verb: a recognized final identifier segment, or the HTTP method.</summary>
    public static string Verb(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return HasVerbSegment(operation)
            ? CSharpNamePolicy.ToPascalCase(operation.Segments[^1])
            : CSharpNamePolicy.ToPascalCase(operation.Method);
    }

    public static string? MethodName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var subject = SubjectOrGroupFallback(operation);
        return subject is null ? null : $"{Verb(operation)}{subject}Async";
    }

    public static string ResponseTypeName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var verb = Verb(operation);
        var verbSuffix = string.Equals(verb, "Get", StringComparison.Ordinal) ? string.Empty : verb;
        return $"{CSharpNamePolicy.ToPascalCase(operation.Segments[0])}{Subject(operation)}{verbSuffix}Response";
    }

    /// <summary>
    /// Route members never restate a root container; client-placed members mirror their
    /// method names so merged client families stay collision-free.
    /// </summary>
    public static string? RouteMemberName(SpecOperation operation, GroupPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (placement is GroupPlacement.Root)
        {
            return $"{Verb(operation)}{Subject(operation)}";
        }

        var subject = SubjectOrGroupFallback(operation);
        return subject is null ? null : $"{Verb(operation)}{subject}";
    }

    public static string? PayloadName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var subject = Subject(operation);
        return subject.Length is 0 ? GroupFallback(operation) : subject;
    }

    public static string RequestTypeName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return $"{CSharpNamePolicy.ToPascalCase(operation.Segments[0])}{Subject(operation)}{Verb(operation)}Request";
    }

    /// <summary>
    /// The operation's subject: the segments between the group and the closing verb.
    /// The final segment also drops when it restates the HTTP method without being a
    /// recognized verb; nothing mid-position is ever dropped.
    /// </summary>
    private static string Subject(SpecOperation operation)
    {
        var end = operation.Segments.Count;
        if (HasVerbSegment(operation)
            || (end > 1 && string.Equals(operation.Segments[^1], operation.Method, StringComparison.Ordinal)))
        {
            end--;
        }

        return string.Concat(operation.Segments.Skip(1).Take(end - 1).Select(CSharpNamePolicy.ToPascalCase));
    }

    private static string? SubjectOrGroupFallback(SpecOperation operation)
    {
        var subject = Subject(operation);
        return subject.Length is 0 ? GroupFallback(operation) : subject;
    }

    /// <summary>An empty subject falls back to the group — pluralized for list operations.</summary>
    private static string? GroupFallback(SpecOperation operation)
    {
        var group = CSharpNamePolicy.ToPascalCase(operation.Segments[0]);
        return string.Equals(Verb(operation), "List", StringComparison.Ordinal) ? Pluralize(group) : group;
    }

    /// <summary>
    /// Naive pluralization only: words needing -es or -ies return <see langword="null"/>
    /// and the operation refuses until the rule set grows deliberately.
    /// </summary>
    private static string? Pluralize(string word)
    {
        if (word[^1] is 's' or 'x' or 'z'
            || word.EndsWith("ch", StringComparison.Ordinal) || word.EndsWith("sh", StringComparison.Ordinal))
        {
            return null;
        }

        if (word.Length > 1 && word[^1] is 'y' && !IsVowel(word[^2]))
        {
            return null;
        }

        return $"{word}s";
    }

    private static bool IsVowel(char letter) => letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';

    private static bool HasVerbSegment(SpecOperation operation) =>
        operation.Segments.Count > 1 && KnownVerbSegments.Contains(operation.Segments[^1], StringComparer.Ordinal);
}
