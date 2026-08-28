using OpenCode.Sdk.Tools.Generator.Binding.Abstractions;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// Marks each pending operation with the bindability the real selection-path binder finds, so
/// wall-free drift among pending operations surfaces as a committed diff instead of accumulating
/// silently — the interim telltale bridging until the operation inventory/assurance-ledger lane
/// standardizes pending-operation tracking.
///
/// Each probe binds one operation in isolation through the same <see cref="ISpecBinder"/>
/// selection uses, under a synthetic single-group, root-placed curation row that answers "would
/// this operation bind if it only needed a curation row?" without asserting what that row should
/// actually look like. A probe that fails for any reason other than the deliberate
/// <see cref="BindingException"/> wall still yields a refused mark instead of failing generation
/// of the already-selected surface over an operation nobody has selected yet.
/// </summary>
internal sealed class PendingOperationBindabilityProbe(ISpecBinder binder)
{
    private const string SyntheticGroupReason =
        "Synthetic single-operation probe curation for the pending-operation bindability telltale; never a real curation row.";

    private readonly ISpecBinder _binder = binder ?? throw new ArgumentNullException(nameof(binder));

    public IReadOnlyList<PendingOperationMark> Probe(SpecDocument document, IReadOnlyList<string> pendingOperationIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pendingOperationIds);

        var operationsById = document.Operations.ToDictionary(static operation => operation.OperationId, StringComparer.Ordinal);
        var marks = new List<PendingOperationMark>(pendingOperationIds.Count);
        foreach (var operationId in pendingOperationIds)
        {
            marks.Add(ProbeOne(document, operationsById[operationId]));
        }

        return marks;
    }

    private PendingOperationMark ProbeOne(SpecDocument document, SpecOperation operation)
    {
        var curation = SyntheticCuration(operation.Segments[0]);
        var selection = new OperationSelection { OperationIds = [operation.OperationId] };

        try
        {
            _ = _binder.Bind(document, selection, curation);
            return new PendingOperationMark { OperationId = operation.OperationId, IsBindable = true };
        }
        catch (BindingException exception)
        {
            return Refused(operation.OperationId, FirstProblem(exception));
        }
        catch (Exception exception)
        {
            // Fail-closed: an operation whose probe fails for any reason other than the
            // deliberate wall above still yields a refused mark carrying the failure, rather
            // than crashing generation of the working selected surface over a pending operation
            // nobody has selected yet.
            return Refused(
                operation.OperationId,
                $"the bindability probe failed unexpectedly ({exception.GetType().Name}): {exception.Message}");
        }
    }

    private static string FirstProblem(BindingException exception) =>
        exception.Errors.Count > 0 ? exception.Errors[0].Problem : exception.Message;

    private static PendingOperationMark Refused(string operationId, string refusalMessage) =>
        new() { OperationId = operationId, IsBindable = false, RefusalMessage = refusalMessage };

    private static GenerationCuration SyntheticCuration(string group) =>
        new()
        {
            Groups = new Dictionary<string, GroupCuration>(StringComparer.Ordinal)
            {
                [group] = new GroupCuration { Placement = GroupPlacement.Root, Reason = SyntheticGroupReason },
            },
            OperationIdentities = [],
            OperationNames = [],
            SchemaNames = [],
            EnvelopePayloadNames = new Dictionary<string, string>(StringComparer.Ordinal),
            SchemaAliases = [],
            TransportOwned = [],
        };
}
