using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

/// <summary>
/// Recognizes which error dialect a schema's own properties spell. The marker names appear here
/// as ingestion's recognition keys and again in <c>Binding/ErrorMarkerPolicy</c>, which owns the
/// binding-side mapping from a recognized dialect to the wire property the emitted converter
/// scans for. Both layers must spell the same two names: a dialect added here without its row
/// there binds to no marker and is refused, and a row there for a dialect this never returns is
/// dead. Change one, check the other.
/// </summary>
internal sealed class ErrorStyleClassifier
{
    public ErrorStyle Classify(IReadOnlyList<SpecProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.Any(static property => property is { Name: "_tag", IsRequired: true, Schema: LiteralNode }))
        {
            return ErrorStyle.EffectTag;
        }

        var hasLiteralName = properties.Any(static property => property is { Name: "name", IsRequired: true, Schema: LiteralNode });
        var hasRequiredData = properties.Any(static property => property is { Name: "data", IsRequired: true });
        return hasLiteralName && hasRequiredData ? ErrorStyle.NameData : ErrorStyle.None;
    }
}
