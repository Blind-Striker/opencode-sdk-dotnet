using System.Text;
using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Refresh;
using OpenCode.Sdk.Tools.Generator.Refresh.Models;
using OpenCode.Sdk.Tools.Serialization;

namespace OpenCode.Sdk.Tools.Tests.Support;

/// <summary>Builders for synchronizer scenarios: documents, manifests, and receipts without raw dumps.</summary>
internal static class RefreshScenarioData
{
    /// <summary>A full commit SHA usable wherever a scenario needs one.</summary>
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>The minimal SNAPSHOT.md shape apply rewrites: one commit row, one date line.</summary>
    public const string SnapshotMarkdown = """
                                           # OpenAPI Snapshot

                                           Date: 2026-08-13

                                           | Fact | Value |
                                           |---|---|
                                           | Commit | aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa |
                                           """;

    /// <summary>Builds an OpenAPI document's bytes through the canonical spec builder.</summary>
    public static byte[] DocumentBytes(Action<SpecDocumentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new SpecDocumentBuilder();
        configure(builder);
        return Encoding.UTF8.GetBytes(builder.BuildJson());
    }

    public static PatchManifest Manifest(string patch = "001-test.patch", string sha256 = "", int order = 1,
        string keyword = "contentSchema", params string[] components) =>
        new()
        {
            Patch = patch,
            Sha256 = sha256,
            UpstreamReport = "https://github.com/anomalyco/opencode/pull/45182",
            Order = order,
            Touches = ["packages/protocol/script/generate-openapi.ts"],
            RepairPredicate = new PatchPredicate
            {
                Type = PatchPredicate.ComponentLacksKeyword,
                Components = components.Length is 0 ? ["V2EventEncoded"] : components,
                Keyword = keyword,
            },
            Retirement = "Raw upstream carries the keyword.",
        };

    /// <summary>Builds one watched upstream source pinned to the given content's hash.</summary>
    public static WatchedSource Watched(string path, string content, string anchor, string anchorType = SourceAnchor.Contains)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new WatchedSource
        {
            Path = path,
            Sha256 = DocumentInspector.Sha256Hex(Encoding.UTF8.GetBytes(content)),
            Behavior = $"the door's dependency on {anchor}",
            Anchor = new SourceAnchor { Type = anchorType, Text = anchor },
        };
    }

    /// <summary>Builds a source watch over the given entries.</summary>
    public static SourceWatch Watch(int schemaVersion, params WatchedSource[] sources) =>
        new() { SchemaVersion = schemaVersion, Sources = sources };

    public static string Serialize(PatchManifest manifest) => JsonSerializer.Serialize(manifest, ToolJsonContext.Default.PatchManifest);

    public static string Serialize(SourceWatch watch) => JsonSerializer.Serialize(watch, ToolJsonContext.Default.SourceWatch);

    public static string Serialize(SnapshotReceipt receipt) => JsonSerializer.Serialize(receipt, ToolJsonContext.Default.SnapshotReceipt);
}
