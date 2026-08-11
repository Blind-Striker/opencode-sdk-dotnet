using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class OperationProjectionTestHost
{
    private readonly GraphKeyBuilder _keys = new();

    public async Task<OperationProjectionResult> ProjectAsync(SpecScenario scenario)
    {
        var result = await ProjectCoreAsync(scenario);
        return result.Refusal is not null
            ? throw new InvalidOperationException("The operation projection was unexpectedly refused.", result.Refusal)
            : result;
    }

    public async Task<IngestionException> ProjectExpectingRefusalAsync(SpecScenario scenario)
    {
        var result = await ProjectCoreAsync(scenario);
        return result.Refusal ?? throw new InvalidOperationException("The operation projection was expected to be refused.");
    }

    private async Task<OperationProjectionResult> ProjectCoreAsync(SpecScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var context = scenario.Build();
        var errors = new IngestionErrorCollector();
        try
        {
            var loaded = await new SpecReader(context.FileSystem).LoadAsync(context.SpecPath, errors, CancellationToken.None);
            var schemaProjector = new SchemaProjector(_keys);
            var state = new ProjectionState(errors, loaded.Document);
            var operations = new OperationProjector(schemaProjector, _keys).Project(loaded, state);

            if (loaded.Document.Components?.Schemas is not null)
            {
                foreach (var (name, schema) in loaded.Document.Components.Schemas)
                {
                    _ = schemaProjector.Project(schema, _keys.Root(name), string.Empty, state);
                }
            }

            errors.ThrowIfAny();
            return new OperationProjectionResult(operations, state.SnapshotGraph(), refusal: null);
        }
        catch (IngestionException exception)
        {
            return new OperationProjectionResult([], new Dictionary<string, SchemaNode>(StringComparer.Ordinal), exception);
        }
    }
}
