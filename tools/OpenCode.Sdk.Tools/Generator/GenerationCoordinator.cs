using System.Globalization;
using System.Text;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Abstractions;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Generator.Ingestion.Abstractions;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Output.Abstractions;

namespace OpenCode.Sdk.Tools.Generator;

internal sealed class GenerationCoordinator(
    ISpecIngestion ingestion,
    OperationSelectionLoader selectionLoader,
    CurationLoader curationLoader,
    ISpecBinder binder,
    IGenerationWriter writer)
{
    private readonly ISpecBinder _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    private readonly CurationLoader _curationLoader = curationLoader ?? throw new ArgumentNullException(nameof(curationLoader));
    private readonly ISpecIngestion _ingestion = ingestion ?? throw new ArgumentNullException(nameof(ingestion));
    private readonly OperationSelectionLoader _selectionLoader = selectionLoader ?? throw new ArgumentNullException(nameof(selectionLoader));
    private readonly IGenerationWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<GenerationReport> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = await _ingestion.IngestAsync(request.SpecPath, cancellationToken).ConfigureAwait(false);
        var selection = await _selectionLoader.LoadAsync(request.ProfilePath, cancellationToken).ConfigureAwait(false);
        var curation = await _curationLoader.LoadAsync(request.CurationPath, cancellationToken).ConfigureAwait(false);
        var plan = _binder.Bind(document, selection, curation);
        var modern = plan.PendingOperations
            .Where(static operation => operation.Surface == SpecSurface.Modern)
            .Select(static operation => operation.OperationId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var legacy = plan.PendingOperations
            .Where(static operation => operation.Surface == SpecSurface.Legacy)
            .Select(static operation => operation.OperationId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var marker = modern.Length + legacy.Length > 0 ? CreatePartialMarker(plan.SelectedOperationIds, modern, legacy) : null;
        var writeResult = await _writer.WriteAsync(
            request.OutputRoot,
            request.ProjectPath,
            SourceEmitter.Emit(plan),
            marker,
            request.Verify,
            cancellationToken).ConfigureAwait(false);
        return new GenerationReport
        {
            SelectedOperationIds = plan.SelectedOperationIds,
            PendingModernOperationIds = modern,
            PendingLegacyOperationIds = legacy,
            WriteResult = writeResult,
        };
    }

    private static string CreatePartialMarker(IReadOnlyList<string> selected, string[] modern, string[] legacy)
    {
        var content = new StringBuilder()
            .AppendLine("Generation is incomplete; packages must not be published.")
            .Append("Selected operations: ")
            .AppendLine(selected.Count.ToString(CultureInfo.InvariantCulture))
            .Append("Pending modern operations: ")
            .AppendLine(modern.Length.ToString(CultureInfo.InvariantCulture))
            .Append("Pending legacy operations: ")
            .AppendLine(legacy.Length.ToString(CultureInfo.InvariantCulture));
        AppendOperations(content, "Selected", selected);
        AppendOperations(content, "Pending modern", modern);
        AppendOperations(content, "Pending legacy", legacy);
        return content.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendOperations(StringBuilder content, string heading, IEnumerable<string> operationIds)
    {
        _ = content.Append(heading).AppendLine(":");
        foreach (var operationId in operationIds)
        {
            _ = content.Append("- ").AppendLine(operationId);
        }
    }
}
