namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>The receipt-facing invariants of one OpenAPI document.</summary>
internal sealed record DocumentStats
{
    /// <summary>Gets the operation ids, sorted ordinally.</summary>
    public required IReadOnlyList<string> OperationIds
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    }

    /// <summary>Gets the SHA-256 over the sorted, newline-joined operation ids.</summary>
    public required string OperationSetDigest { get; init; }

    /// <summary>Gets the number of component schemas.</summary>
    public required int ComponentCount { get; init; }

    /// <summary>Gets the document-wide <c>contentSchema</c> occurrence count.</summary>
    public required int ContentSchemaCount { get; init; }
}
