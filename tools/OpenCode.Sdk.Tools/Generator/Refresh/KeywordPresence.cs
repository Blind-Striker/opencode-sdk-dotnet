namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>The three answers a component-keyword probe can give.</summary>
internal enum KeywordPresence
{
    /// <summary>The component exists and lacks the keyword — the repair is still needed.</summary>
    Lacks = 0,

    /// <summary>The component exists and carries the keyword — the repair has landed upstream.</summary>
    Carries = 1,

    /// <summary>The component is absent from the document — the patch needs human review.</summary>
    ComponentMissing = 2,
}
