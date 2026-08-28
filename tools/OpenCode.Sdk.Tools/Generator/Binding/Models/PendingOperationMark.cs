namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// The bindability the real selection-path binder finds for one pending operation: either it
/// binds standalone today (adding it to selection would need only a curation row), or a wall
/// refuses it and this carries that wall's verbatim first refusal message.
/// </summary>
internal sealed record PendingOperationMark
{
    public required string OperationId { get; init; }

    public required bool IsBindable { get; init; }

    /// <summary>The verbatim refusal message when <see cref="IsBindable"/> is false; null otherwise.</summary>
    public string? RefusalMessage { get; init; }
}
