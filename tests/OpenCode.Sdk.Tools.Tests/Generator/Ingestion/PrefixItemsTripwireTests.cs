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
        // Config.plugin tuple — this test must fail loudly before that can ship.
        var fileSystem = new RealFileSystem();
        var specPath = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi.json");
        var errors = new IngestionErrorCollector();

        var loaded = await new SpecReader(fileSystem).LoadAsync(specPath, errors, CancellationToken.None);

        var plugin = (OpenApiSchema)loaded.Document.Components!.Schemas!["Config"].Properties!["plugin"];
        var tupleBranch = (OpenApiSchema)plugin.Items!.AnyOf![1];
        await Assert.That(tupleBranch.UnrecognizedKeywords!.Keys).Contains("prefixItems");
    }
}
