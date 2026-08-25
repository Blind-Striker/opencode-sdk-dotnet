using System.Text.Json;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// The B-1 mechanism leg of the standing walkthrough: the bodyless POSTs, the PUT family
/// (mcp add, pty update, the instructions entry), and the batch's new error types carried
/// live over the NoThrow spine.
/// </summary>
internal static class MechanismActionsWalkthrough
{
    public static async Task RunAsync(OpenCodeClient client, SessionClient handle)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(handle);

        var interrupt = await handle.PostInterruptAsync().ConfigureAwait(false);

        Console.WriteLine($"interrupt: status={interrupt.Status} isError={interrupt.IsError}");

        var revertClear = await handle.PostRevertClearAsync(OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine(revertClear.IsError
            ? $"revert-clear: status={revertClear.Status} error={ErrorName(revertClear)}"
            : $"revert-clear: status={revertClear.Status}");

        using var value = JsonDocument.Parse("\"answer tersely\"");
        var entryPut = await handle.PutInstructionsEntryAsync(
                "style",
                new SessionInstructionsEntryPutRequest { Value = value.RootElement })
            .ConfigureAwait(false);

        Console.WriteLine($"instructions-put: status={entryPut.Status}");

        var entryRemove = await handle.RemoveInstructionsEntryAsync("style", OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine($"instructions-remove: status={entryRemove.Status} isError={entryRemove.IsError}");

        var formCancel = await handle.PostFormCancelAsync("frm_missing", OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine(formCancel.IsError
            ? $"form-cancel: status={formCancel.Status} error={ErrorName(formCancel)}"
            : $"form-cancel: status={formCancel.Status}");

        var mcp = client.McpServers.GetMcpServerClient("sandbox-echo");
        var mcpAdd = await mcp.PutAddAsync(
                new McpAddPutRequest
                {
                    Config = new McpLocalConfig { Command = ["bun", "--version"], Disabled = true },
                },
                OpenCodeRequestOptions.NoThrow)
            .ConfigureAwait(false);

        Console.WriteLine(mcpAdd.IsError
            ? $"mcp-add: status={mcpAdd.Status} error={ErrorName(mcpAdd)}"
            : $"mcp-add: status={mcpAdd.Status}");

        var mcpDisconnect = await mcp.PostDisconnectAsync(null, OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine(mcpDisconnect.IsError
            ? $"mcp-disconnect: status={mcpDisconnect.Status} error={ErrorName(mcpDisconnect)}"
            : $"mcp-disconnect: status={mcpDisconnect.Status}");

        var mcpRemove = await mcp.RemoveMcpServerAsync(null, OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine($"mcp-remove: status={mcpRemove.Status} isError={mcpRemove.IsError}");

        var pty = await client.Ptys.CreatePtyAsync(new PtyCreateRequest
        {
            Command = "pwsh",
            Title = "sdk mechanism demo",
        }).ConfigureAwait(false);

        Console.WriteLine($"pty-create: status={pty.Status} id={pty.Pty.Id} title={pty.Pty.Title}");

        var ptyHandle = client.Ptys.GetPtyClient(pty.Pty.Id);
        var ptyUpdate = await ptyHandle.PutUpdateAsync(new PtyUpdatePutRequest
        {
            Title = "renamed by PUT",
        }).ConfigureAwait(false);

        Console.WriteLine($"pty-update: status={ptyUpdate.Status} title={ptyUpdate.Update.Title}");

        var ptyRemove = await ptyHandle.RemovePtyAsync(null, OpenCodeRequestOptions.NoThrow).ConfigureAwait(false);

        Console.WriteLine($"pty-remove: status={ptyRemove.Status} isError={ptyRemove.IsError}");
    }

    private static string ErrorName(OpenCodeResponse response) => response.Error?.GetType().Name ?? "<untyped>";
}
