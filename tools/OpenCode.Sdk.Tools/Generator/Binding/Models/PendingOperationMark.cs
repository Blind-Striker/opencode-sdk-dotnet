namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// The bindability the real selection-path binder finds for one pending operation: either it
/// binds standalone today (adding it to selection would need only a curation row), or one or more
/// walls refuse it and this carries every independent wall's verbatim problem text, in binder
/// order and deduplicated by problem text, joined by <c>"; "</c>.
/// </summary>
internal sealed record PendingOperationMark
{
    public required string OperationId { get; init; }

    public required bool IsBindable { get; init; }

    /// <summary>The verbatim, semicolon-joined refusal messages when <see cref="IsBindable"/> is false; null otherwise.</summary>
    public string? RefusalMessage { get; init; }
}
