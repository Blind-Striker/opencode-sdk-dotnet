using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Projection;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class SchemaProjectionTestHost
{
    private readonly GraphKeyBuilder _keys = new();
    private readonly SchemaWallPolicy _wall = new();

    public async Task<SchemaProjectionResult> ProjectAsync(SpecScenario scenario)
    {
        var result = await ProjectCoreAsync(scenario);
        return result.Refusal is not null
            ? throw new InvalidOperationException("The schema projection was unexpectedly refused.", result.Refusal)
            : result;
    }

    public async Task<IngestionException> ProjectExpectingRefusalAsync(SpecScenario scenario)
    {
        var result = await ProjectCoreAsync(scenario);
        return result.Refusal ?? throw new InvalidOperationException("The schema projection was expected to be refused.");
    }

    private async Task<SchemaProjectionResult> ProjectCoreAsync(SpecScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var context = scenario.Build();
        var errors = new IngestionErrorCollector();
        try
        {
            var loaded = await new SpecReader(context.FileSystem).LoadAsync(context.SpecPath, errors, CancellationToken.None);
            var projector = new SchemaProjector(_wall, _keys);
            var rawPointerLookup = ImmutableDictionary<string, JsonNode>.Empty;
            var state = new ProjectionState(errors, loaded.Document, rawPointerLookup);

            if (loaded.Document.Components?.Schemas is not null)
            {
                foreach (var (name, schema) in loaded.Document.Components.Schemas)
                {
                    _ = projector.Project(schema, _keys.Root(name), string.Empty, state);
                }
            }

            errors.ThrowIfAny();
            return new SchemaProjectionResult(state.SnapshotGraph(), refusal: null);
        }
        catch (IngestionException exception)
        {
            return new SchemaProjectionResult(new Dictionary<string, SchemaNode>(StringComparer.Ordinal), exception);
        }
    }
}
