namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

/// <summary>
/// A base64-encoded string on the wire, materialized as bytes. The conversion is a represented
/// token conversion the runtime performs (ADR-0014) — System.Text.Json decodes base64 natively
/// for <c>ReadOnlyMemory&lt;byte&gt;</c> — so the generated shape stays faithful to the document
/// without a converter of its own.
/// </summary>
internal sealed record BinaryTypeReferencePlan : TypeReferencePlan
{
    public override bool IsCollection => false;
}
