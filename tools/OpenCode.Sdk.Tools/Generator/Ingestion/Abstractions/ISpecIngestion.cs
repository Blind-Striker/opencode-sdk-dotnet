using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Abstractions;

/// <summary>Ingests the pinned OpenAPI document into the generator's semantic model.</summary>
public interface ISpecIngestion
{
    /// <summary>Loads, validates, and projects the spec; refuses with batched located errors.</summary>
    public Task<SpecDocument> IngestAsync(string specPath, CancellationToken cancellationToken);
}
