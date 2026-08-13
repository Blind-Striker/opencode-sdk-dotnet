namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>Identifies how a bound query parameter value maps between C# and the wire.</summary>
internal enum QueryValueKind
{
    /// <summary>A plain string carried verbatim.</summary>
    Text = 0,

    /// <summary>A positive count exposed as <c>int?</c> and written invariantly to the wire string.</summary>
    PositiveCount = 1,

    /// <summary>The asc/desc order enum exposed as the shared <c>ListOrder</c> spine type.</summary>
    ListOrder = 2,

    /// <summary>The parent-session filter whose wire admits an identifier or the literal <c>"null"</c>.</summary>
    SessionParentFilter = 3,
}
