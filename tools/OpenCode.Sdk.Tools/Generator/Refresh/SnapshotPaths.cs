namespace OpenCode.Sdk.Tools.Generator.Refresh;

/// <summary>Repository-relative paths the synchronizer owns; the tool runs from the repository root.</summary>
internal static class SnapshotPaths
{
    public const string AcceptedDocument = "spec/openapi.json";

    public const string CommittedReceipt = "spec/receipt.json";

    public const string PatchesRoot = "spec/patches";

    public const string SnapshotMarkdown = "spec/SNAPSHOT.md";

    public const string Submodule = "external/opencode";

    public const string UpstreamArtifact = "packages/protocol/openapi.json";

    public const string UpstreamProtocolPackage = "packages/protocol";

    public const string ScratchRoot = ".scratchpad/refresh";
}
