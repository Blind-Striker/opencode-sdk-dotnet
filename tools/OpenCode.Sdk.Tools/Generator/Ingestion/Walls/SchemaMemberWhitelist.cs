using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class SchemaMemberWhitelist
{
    private static readonly FrozenSet<string> AdmittedMembers = new[]
    {
        nameof(OpenApiSchema.AdditionalProperties), nameof(OpenApiSchema.AdditionalPropertiesAllowed), nameof(OpenApiSchema.AnyOf),
        nameof(OpenApiSchema.Const), nameof(OpenApiSchema.ContentEncoding), nameof(OpenApiSchema.ContentMediaType),
        nameof(OpenApiSchema.ContentSchema), nameof(OpenApiSchema.Description), nameof(OpenApiSchema.Enum), nameof(OpenApiSchema.Format),
        nameof(OpenApiSchema.Items), nameof(OpenApiSchema.OneOf), nameof(OpenApiSchema.PatternProperties), nameof(OpenApiSchema.Properties),
        nameof(OpenApiSchema.Required), nameof(OpenApiSchema.Type),
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> KnownIgnoredMembers = new[]
    {
        nameof(OpenApiSchema.ExclusiveMinimum), nameof(OpenApiSchema.Maximum), nameof(OpenApiSchema.MaxItems), nameof(OpenApiSchema.Minimum),
        nameof(OpenApiSchema.MinItems), nameof(OpenApiSchema.Pattern),
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SeparatelyCheckedMembers =
        new[] { nameof(OpenApiSchema.Extensions), nameof(OpenApiSchema.UnrecognizedKeywords), }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> WireNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(OpenApiSchema.Anchor)] = "$anchor",
        [nameof(OpenApiSchema.Comment)] = "$comment",
        [nameof(OpenApiSchema.Definitions)] = "$defs",
        [nameof(OpenApiSchema.DynamicAnchor)] = "$dynamicAnchor",
        [nameof(OpenApiSchema.DynamicRef)] = "$dynamicRef",
        [nameof(OpenApiSchema.Id)] = "$id",
        [nameof(OpenApiSchema.Schema)] = "$schema",
        [nameof(OpenApiSchema.UnevaluatedPropertiesSchema)] = "unevaluatedProperties",
        [nameof(OpenApiSchema.Vocabulary)] = "$vocabulary",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly OpenApiSchema _defaultSchema = new();

    private readonly PropertyInfo[] _schemaMembers =
    [
        .. typeof(OpenApiSchema)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(property => property.Name, StringComparer.Ordinal),
    ];

    public void Check(OpenApiSchema schema, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        foreach (var member in _schemaMembers)
        {
            if (IsExempt(member.Name) || !IsPopulated(member, schema))
            {
                continue;
            }

            errors.Add(location, $"schema keyword '{GetWireName(member.Name)}' is not supported");
        }
    }

    private static string GetWireName(string memberName)
    {
        if (WireNames.TryGetValue(memberName, out var wireName))
        {
            return wireName;
        }

        return $"{char.ToLowerInvariant(memberName[0])}{memberName[1..]}";
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string)
        && typeof(IEnumerable).IsAssignableFrom(type);

    private static bool IsExempt(string memberName) =>
        AdmittedMembers.Contains(memberName)
        || KnownIgnoredMembers.Contains(memberName)
        || SeparatelyCheckedMembers.Contains(memberName)
        || string.Equals(memberName, nameof(OpenApiSchema.Metadata), StringComparison.Ordinal);

    private bool IsPopulated(PropertyInfo member, OpenApiSchema schema)
    {
        var value = member.GetValue(schema);
        if (value is null || Equals(value, member.GetValue(_defaultSchema)))
        {
            return false;
        }

        if (!IsCollection(member.PropertyType))
        {
            return true;
        }

        var enumerator = ((IEnumerable)value).GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
