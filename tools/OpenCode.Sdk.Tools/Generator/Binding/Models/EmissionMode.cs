namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// How a curated group's client family reaches callers. <see cref="InternalRaw"/> keeps the
/// generated family internal so a hand-written public door owns the family's surface
/// (ADR-0021); the default keeps the generated family public.
/// </summary>
internal enum EmissionMode
{
    Public = 0,
    InternalRaw = 1,
}
