using System.Text.Json.Serialization;
using OpenCode.Sdk.Tools.Benchmarks.Models;

namespace OpenCode.Sdk.Tools.Benchmarks;

/// <summary>Reads BenchmarkDotNet full JSON exports. Separate from <c>ToolJsonContext</c>: the
/// exports carry many members this tool never consumes, so this context must skip unmapped members
/// while the tool's own documents disallow them.</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BenchmarkReportDocument))]
internal sealed partial class BenchmarkReportJsonContext : JsonSerializerContext;
