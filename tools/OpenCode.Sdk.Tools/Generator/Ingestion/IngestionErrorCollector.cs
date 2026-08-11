using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion;

internal sealed class IngestionErrorCollector
{
    private readonly List<IngestionError> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public void Add(string location, string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);

        _errors.Add(new IngestionError(location, problem));
    }

    public void ThrowIfAny()
    {
        if (HasErrors)
        {
            throw new IngestionException(_errors);
        }
    }
}
