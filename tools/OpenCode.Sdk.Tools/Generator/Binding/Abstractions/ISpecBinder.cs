using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding.Abstractions;

internal interface ISpecBinder
{
    public EmitPlan Bind(SpecDocument document, OperationSelection selection, GenerationCuration curation);
}
