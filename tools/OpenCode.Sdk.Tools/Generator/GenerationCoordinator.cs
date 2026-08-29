using System.Globalization;
using System.Text;
using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Abstractions;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Generator.Ingestion.Abstractions;
using OpenCode.Sdk.Tools.Output;
using OpenCode.Sdk.Tools.Output.Abstractions;

namespace OpenCode.Sdk.Tools.Generator;

internal sealed class GenerationCoordinator(
    ISpecIngestion ingestion,
    OperationSelectionLoader selectionLoader,
    CurationLoader curationLoader,
    ISpecBinder binder,
    PendingOperationBindabilityProbe pendingProbe,
    IGenerationWriter writer)
{
    private readonly ISpecIngestion _ingestion = ingestion ?? throw new ArgumentNullException(nameof(ingestion));
    private readonly OperationSelectionLoader _selectionLoader = selectionLoader ?? throw new ArgumentNullException(nameof(selectionLoader));
    private readonly CurationLoader _curationLoader = curationLoader ?? throw new ArgumentNullException(nameof(curationLoader));
    private readonly ISpecBinder _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    private readonly PendingOperationBindabilityProbe _pendingProbe = pendingProbe ?? throw new ArgumentNullException(nameof(pendingProbe));
    private readonly IGenerationWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<GenerationReport> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Curation loads first: its operation-identity rows are an ingestion input (ADR-0013),
        // so their static validity is checked before the document is read.
        var curation = await _curationLoader.LoadAsync(request.CurationPath, cancellationToken).ConfigureAwait(false);
        var operationIdentities = OperationIdentityPolicy.BuildMap(curation);
        var document = await _ingestion.IngestAsync(request.SpecPath, operationIdentities, cancellationToken).ConfigureAwait(false);
        var selection = await _selectionLoader.LoadAsync(request.ProfilePath, cancellationToken).ConfigureAwait(false);

        var plan = _binder.Bind(document, selection, curation);

        var pending = plan
            .PendingOperations
            .Select(static operation => operation.OperationId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Marking runs the same selection-path binder over each pending operation in isolation
        // (ADR-driven bridge telltale), so a wall-free operation shows up as a committed diff
        // instead of accumulating silently.
        var pendingMarks = pending.Length > 0 ? _pendingProbe.Probe(document, pending) : [];
        // Transport-owned operations are not pending — the binder keeps them out — so they are
        // never probed; the marker lists them beside the pending map as fingerprint-pinned.
        var marker = pending.Length > 0
            ? CreatePartialMarker(plan.SelectedOperationIds, pendingMarks, plan.TransportOwnedOperationIds)
            : null;

        // The admitted family folders are the plan's own container names — never an open glob.
        var familyFolders = plan.Clients
            .SelectMany(static client => client.Operations)
            .Select(static operation => operation.RouteContainerName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var writeResult = await _writer
            .WriteAsync(
                new GenerationWriteRequest
                {
                    OutputRoot = request.OutputRoot,
                    ProjectPath = request.ProjectPath,
                    Sources = SourceEmitter.Emit(plan),
                    FamilyFolders = familyFolders,
                    PartialMarkerContent = marker,
                    Verify = request.Verify,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new GenerationReport
        {
            SelectedOperationIds = plan.SelectedOperationIds,
            PendingOperationIds = pending,
            TransportOwnedOperationIds = plan.TransportOwnedOperationIds,
            WriteResult = writeResult,
        };
    }

    private static string CreatePartialMarker(IReadOnlyList<string> selected, IReadOnlyList<PendingOperationMark> pendingMarks,
        IReadOnlyList<string> transportOwned)
    {
        var content = new StringBuilder()
            .AppendLine("Generation is incomplete; packages must not be published.")
            .Append("Selected operations: ")
            .AppendLine(selected.Count.ToString(CultureInfo.InvariantCulture))
            .Append("Pending operations: ")
            .AppendLine(pendingMarks.Count.ToString(CultureInfo.InvariantCulture))
            .Append("Transport-owned operations: ")
            .AppendLine(transportOwned.Count.ToString(CultureInfo.InvariantCulture));
        AppendOperations(content, "Selected", selected);
        AppendOperations(content, "Pending", pendingMarks.Select(FormatPendingLine));
        AppendOperations(content, "Transport-owned", transportOwned.Select(FormatTransportOwnedLine));

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

    private static string FormatPendingLine(PendingOperationMark mark) =>
        $"{mark.OperationId} {(mark.IsBindable ? "[bindable]" : $"[refused: {mark.RefusalMessage}]")}";

    /// <summary>The curation validator has already proven the row's subtree fingerprint on this bind.</summary>
    private static string FormatTransportOwnedLine(string operationId) => $"{operationId} [fingerprint-pinned]";
}
