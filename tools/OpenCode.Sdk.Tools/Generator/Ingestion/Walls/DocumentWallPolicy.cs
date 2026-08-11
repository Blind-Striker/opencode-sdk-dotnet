using Microsoft.OpenApi;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class DocumentWallPolicy
{
    private readonly HostMemberWhitelist<OpenApiComponents> _componentMembers = new("components", [nameof(OpenApiComponents.Schemas)]);

    private readonly HostMemberWhitelist<OpenApiDocument> _documentMembers = new("document",
    [
        nameof(OpenApiDocument.BaseUri),
        nameof(OpenApiDocument.Components),
        nameof(OpenApiDocument.Info),
        nameof(OpenApiDocument.Metadata),
        nameof(OpenApiDocument.Paths),
        nameof(OpenApiDocument.Security),
        nameof(OpenApiDocument.Self),
        nameof(OpenApiDocument.Tags),
        nameof(OpenApiDocument.Workspace),
    ]);

    public void Check(OpenApiDocument document, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(errors);

        _documentMembers.Check(document, "document", errors);
        if (document.Components is not null)
        {
            _componentMembers.Check(document.Components, "document/components", errors);
        }
    }
}
