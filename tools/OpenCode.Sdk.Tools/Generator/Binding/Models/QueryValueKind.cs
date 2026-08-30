namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>Identifies how a bound query parameter value maps between C# and the wire.</summary>
internal enum QueryValueKind
{
    /// <summary>A plain string carried verbatim.</summary>
    Text = 0,

    /// <summary>The asc/desc order enum exposed as the shared <c>ListOrder</c> spine type.</summary>
    ListOrder = 1,

    /// <summary>The exact true/false string enum exposed without converting it to a C# boolean.</summary>
    BooleanText = 2,

    /// <summary>The parent-session filter whose wire admits an identifier or the literal <c>"null"</c>.</summary>
    SessionParentFilter = 3,

    /// <summary>The deepObject location selector exposed as the shared <c>LocationSelector</c> spine type.</summary>
    Location = 4,

    /// <summary>A string enum outside the spine profiles, exposed as its own generated C# enum.</summary>
    Enum = 5,
}
