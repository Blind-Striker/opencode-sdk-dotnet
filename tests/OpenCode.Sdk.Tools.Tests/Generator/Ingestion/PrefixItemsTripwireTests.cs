using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

public sealed class PrefixItemsTripwireTests
{
    [Test]
    public async Task Reader_Should_Retain_PrefixItems_As_Unrecognized_Keyword_At_The_Admitted_Site()
    {
        // Library-upgrade tripwire: the prefixItems adapter reads the raw keyword the pinned
        // Microsoft.OpenApi retains as unrecognized. A newer library that types prefixItems
        // would leave UnrecognizedKeywords empty, blind the adapter, and silently degrade the
        // Form.Fields non-empty array — this test must fail loudly before that can ship.
        var fileSystem = new RealFileSystem();
        var specPath = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi.json");
        var errors = new IngestionErrorCollector();

        var loaded = await new SpecReader(fileSystem).LoadAsync(specPath, errors, CancellationToken.None);

        var fields = (OpenApiSchema)loaded.Document.Components!.Schemas!["Form.Fields"];
        await Assert.That(fields.UnrecognizedKeywords!.Keys).Contains("prefixItems");
    }
}
