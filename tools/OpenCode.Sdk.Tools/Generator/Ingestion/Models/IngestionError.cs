namespace OpenCode.Sdk.Tools.Generator.Ingestion.Models;

/// <summary>Describes one failure discovered while ingesting an OpenAPI document.</summary>
/// <param name="Location">The document location associated with the failure.</param>
/// <param name="Problem">A description of the failure.</param>
public sealed record IngestionError(string Location, string Problem);
