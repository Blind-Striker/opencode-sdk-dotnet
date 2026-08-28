using System.Text.Json;

namespace OpenCode.Sdk.TestSupport;

/// <summary>
/// Composes the drive backend's JSON-RPC requests (protocol source of truth:
/// packages/protocol/src/simulation.ts at the pin — Handshake.Params :98-107, ChunkParams :549,
/// FinishParams :552, DisconnectParams :564; llm.attach/llm.pending carry no params :576-579).
/// Every request carries a numeric id: id-less requests get no response (simulation.ts:46-49).
/// </summary>
internal static class DriveProtocol
{
    public static byte[] Handshake(long id) =>
        Compose(id, "simulation.handshake", static writer =>
        {
            writer.WriteStartObject("params");
            writer.WriteStartObject("client");
            writer.WriteString("name", "opencode-sdk-dotnet");
            writer.WriteString("version", "tests");
            writer.WriteEndObject();
            writer.WriteString("expectedRole", "backend");
            writer.WriteStartArray("offeredVersions");
            writer.WriteNumberValue(1);
            writer.WriteEndArray();
            writer.WriteStartArray("requiredCapabilities");
            writer.WriteStringValue("llm.attach");
            writer.WriteStringValue("llm.request");
            writer.WriteStringValue("llm.chunk");
            writer.WriteStringValue("llm.finish");
            writer.WriteEndArray();
            writer.WriteStartArray("optionalCapabilities");
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    public static byte[] Attach(long id) => Compose(id, "llm.attach", writeParams: null);

    public static byte[] ChunkText(long id, string invocationId, IReadOnlyList<string> deltas) =>
        Compose(id, "llm.chunk", writer =>
        {
            writer.WriteStartObject("params");
            writer.WriteString("id", invocationId);
            writer.WriteStartArray("items");
            foreach (var delta in deltas)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "textDelta");
                writer.WriteString("text", delta);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    public static byte[] Finish(long id, string invocationId, string reason) =>
        Compose(id, "llm.finish", writer =>
        {
            writer.WriteStartObject("params");
            writer.WriteString("id", invocationId);
            writer.WriteString("reason", reason);
            writer.WriteEndObject();
        });

    public static byte[] Disconnect(long id, string invocationId) =>
        Compose(id, "llm.disconnect", writer =>
        {
            writer.WriteStartObject("params");
            writer.WriteString("id", invocationId);
            writer.WriteEndObject();
        });

    public static byte[] Pending(long id) => Compose(id, "llm.pending", writeParams: null);

    private static byte[] Compose(long id, string method, Action<Utf8JsonWriter>? writeParams)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writeParams?.Invoke(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
