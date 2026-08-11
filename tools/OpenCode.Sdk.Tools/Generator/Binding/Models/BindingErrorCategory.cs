namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>Identifies the generator stage that refused a binding input.</summary>
public enum BindingErrorCategory
{
    /// <summary>The checked-in operation selection is invalid.</summary>
    Selection = 0,

    /// <summary>The public API curation is invalid or incomplete.</summary>
    Curation = 1,

    /// <summary>A selected schema cannot be represented by the emitter plan.</summary>
    Schema = 2,

    /// <summary>A selected wire name cannot be mapped to a unique C# identifier.</summary>
    Naming = 3,
}
