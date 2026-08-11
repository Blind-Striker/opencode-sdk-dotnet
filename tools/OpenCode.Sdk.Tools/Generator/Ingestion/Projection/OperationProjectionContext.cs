using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Projection;

internal sealed class OperationProjectionContext(JsonNode rawPaths, ProjectionState state)
{
    private readonly HashSet<string> _operationIds = new(StringComparer.Ordinal);
    private readonly List<SpecOperation> _operations = [];
    private readonly JsonNode _rawPaths = rawPaths ?? throw new ArgumentNullException(nameof(rawPaths));

    public ProjectionState State { get; } = state ?? throw new ArgumentNullException(nameof(state));

    public void Add(SpecOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Add(operation);
    }

    public bool TryGetRawPath(string path, [NotNullWhen(true)] out JsonObject? rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_rawPaths[path] is JsonObject found)
        {
            rawPath = found;
            return true;
        }

        rawPath = null;
        return false;
    }

    public bool TryRegisterOperationId(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return _operationIds.Add(operationId);
    }

    public IReadOnlyList<SpecOperation> Snapshot() => Array.AsReadOnly([.. _operations]);
}
