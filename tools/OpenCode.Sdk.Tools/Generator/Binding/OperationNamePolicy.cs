using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>Derives C# member and type names for one bound operation from its identifier segments.</summary>
internal static class OperationNamePolicy
{
    public static string MethodName(SpecOperation operation, GroupPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var subject = Subject(operation);
        if (subject.Length is 0 && placement is GroupPlacement.Root)
        {
            subject = CSharpNamePolicy.ToPascalCase(operation.Segments[0]);
        }

        return $"{CSharpNamePolicy.ToPascalCase(operation.Method)}{subject}Async";
    }

    public static string ResponseTypeName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return $"{CSharpNamePolicy.ToPascalCase(operation.Segments[0])}{Subject(operation)}Response";
    }

    public static string RouteMemberName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return $"{CSharpNamePolicy.ToPascalCase(operation.Method)}{Subject(operation)}";
    }

    public static string PayloadName(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var subject = Subject(operation);
        return subject.Length is 0 ? CSharpNamePolicy.ToPascalCase(operation.Segments[0]) : subject;
    }

    /// <summary>The operation's subject: every segment after the group that does not restate the HTTP method.</summary>
    private static string Subject(SpecOperation operation) =>
        string.Concat(operation.Segments
            .Skip(1)
            .Where(segment => !string.Equals(segment, operation.Method, StringComparison.Ordinal))
            .Select(CSharpNamePolicy.ToPascalCase));
}
