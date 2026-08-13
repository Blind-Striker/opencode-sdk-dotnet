using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class BindingTestHost
{
    private readonly SpecBinder _binder;

    public BindingTestHost()
    {
        _binder = new SpecBinder(
            new ReachableSchemaCollector(),
            new CurationValidator(),
            new SchemaAliasApplier(),
            new SchemaNameResolver(),
            new SchemaPlanBinder(),
            new OperationPlanBinder());
    }

    public EmitPlan Bind(SpecDocument document, OperationSelection selection, GenerationCuration curation)
    {
        return _binder.Bind(document, selection, curation);
    }

    public static async Task<SpecDocument> IngestAsync(SpecScenario scenario, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var context = scenario.Build();
        return await new SpecIngestion(context.FileSystem).IngestAsync(context.SpecPath, cancellationToken);
    }

    public async Task<EmitPlan> BindPinnedAsync(CancellationToken cancellationToken = default)
    {
        var fileSystem = new RealFileSystem();
        var fixtureRoot = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var document = await new SpecIngestion(fileSystem)
            .IngestAsync(fileSystem.Path.Combine(fixtureRoot, "openapi.json"), cancellationToken);
        var selection = await new OperationSelectionLoader(fileSystem)
            .LoadAsync(fileSystem.Path.Combine(fixtureRoot, "generation-profile.txt"), cancellationToken);
        var curation = await new CurationLoader(fileSystem)
            .LoadAsync(fileSystem.Path.Combine(fixtureRoot, "curation.json"), cancellationToken);
        return Bind(document, selection, curation);
    }
}
