using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

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
