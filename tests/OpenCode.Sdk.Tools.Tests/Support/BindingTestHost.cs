using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class BindingTestHost
{
    private readonly SpecBinder _binder;

    public BindingTestHost()
    {
        var schemaNames = new SchemaNameResolver();
        _binder = new SpecBinder(
            new ReachableSchemaCollector(),
            new CurationValidator(),
            new SchemaPlanBinder(schemaNames));
    }

    public EmitPlan Bind(SpecDocument document, OperationSelection selection, GenerationCuration curation)
    {
        return _binder.Bind(document, selection, curation);
    }
}
