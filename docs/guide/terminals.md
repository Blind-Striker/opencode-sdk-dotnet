# 🖥️ Terminals

opencode has two terminal families, and they are genuinely different animals:

| | **PTY** | **Persistent PTY** |
|---|---|---|
| Owned by | the server process | the `opencode-pty` daemon |
| Keyed by | its own id | a session id |
| Survives a server restart | no | yes, through a handoff |
| Output on the wire | text frames | raw bytes |
| Input | `WriteAsync(string)` | `WriteAsync(ReadOnlyMemory<byte>)` + `ResizeAsync` |
| Available on Windows | ✅ yes | ❌ no — [see the platform note](#-the-windows-platform-note) |

Both live sessions ride a **WebSocket**, not the HTTP pipeline. They are the only two doors in the
SDK that do, and they are hand-written for exactly that reason — a socket upgrade cannot be
generated from an OpenAPI document.

- [🧵 A normal PTY, end to end](#-a-normal-pty-end-to-end)
- [♻️ Resuming with a cursor](#️-resuming-with-a-cursor)
- [🧷 Persistent PTYs](#-persistent-ptys)
- [🪟 The Windows platform note](#-the-windows-platform-note)
- [🎫 Connect tokens](#-connect-tokens)

## 🧵 A normal PTY, end to end

Create → take a handle → connect → read frames while you write input:

```csharp
var created = await client.Ptys.CreatePtyAsync(new PtyCreateRequest { Title = "sdk demo" });
var pty = client.Ptys.GetPtyClient(created.Pty.Id);

await using var terminal = await pty.ConnectAsync();

// A terminal's Enter key is a carriage return; a line feed renders the text but never submits it.
await terminal.WriteAsync("echo hello\r");

long? cursor = null;

await foreach (var frame in terminal.ReadAsync(cancellationToken))
{
    switch (frame)
    {
        case PtyOutputFrame output:
            Console.Write(output.Text);
            break;
        case PtyCursorFrame control:
            cursor ??= control.Cursor;
            break;
    }
}
```

`PtySession` is small on purpose — `ReadAsync`, `WriteAsync`, `DisposeAsync` — and it **owns its
socket**, so disposing it is how you end the connection. `await using` does that for you.

`ReadAsync` yields `PtyFrame`, which has exactly two shapes:

- **`PtyOutputFrame`** — terminal output, already decoded to `Text`.
- **`PtyCursorFrame`** — one control frame, sent once when the replay ends, carrying the absolute
  output `Cursor` you have now caught up to.

Three rules worth knowing before your first surprise:

1. **Enter is `\r`.** `WriteAsync` sends exactly the bytes you give it. A command ending in `\n`
   renders on the terminal and then sits there forever, unsubmitted.
2. **One read at a time.** A session carries one active read enumeration; starting a second
   concurrently throws `InvalidOperationException`. Writes are serialized for you.
3. **There is no end-of-command marker on this wire.** A terminal just goes quiet. If you need to
   know a command finished, either wait for the stream to settle or ask `GetPtyAsync` for the
   PTY's status and exit code — the exit code is *not* on the socket.

Closing:

```csharp
var removed = await pty.RemovePtyAsync(null, OpenCodeRequestOptions.NoThrow);

Console.WriteLine($"removed: status={removed.Status} isError={removed.IsError}");
```

Removing a PTY while a read is in flight ends that read as a **normal close**, not as a fault. An
abnormal close, or a PTY that had already exited, throws `OpenCodeTransportException` naming the
reason.

## ♻️ Resuming with a cursor

A PTY outlives any connection to it, and the server retains its recent output. `PtyConnectOptions.Cursor`
picks what you get on attach:

| `Cursor` | You receive |
|---|---|
| omitted (`null`) | the whole retained buffer, replayed |
| `-1` | live output only, no replay |
| `0` or above | everything after that absolute output position |

```csharp
await using var resumed = await pty.ConnectAsync(new PtyConnectOptions { Cursor = cursor });
```

Feed it the `Cursor` from the previous connection's `PtyCursorFrame` and you get exactly what you
missed — no gap, no duplicate. An out-of-range value is refused client-side rather than silently
degrading into a full replay.

## 🧷 Persistent PTYs

These terminals belong to the `opencode-pty` daemon, are keyed to a **session**, and survive a
server restart. Creation is session-keyed; everything id-keyed sits on a handle:

```csharp
var created = await client.PersistentPtys.CreatePersistentPtyAsync(sessionId, new PersistentPtyCreateRequest
{
    Args = [],
    Env = new Dictionary<string, string>(StringComparer.Ordinal),
    Title = "sdk persistent demo",
}, OpenCodeRequestOptions.NoThrow);

if (created is { Status: 503, Error: ServiceUnavailableError { Service: "opencode-pty" } })
{
    Console.WriteLine("the opencode-pty daemon is not available on this host");
    return;
}

var terminal = client.PersistentPtys.GetPersistentPtyClient(created.PersistentPty.Id);
```

Then attach and drive it:

```csharp
await using var attached = await terminal.ConnectAsync(new PersistentPtyConnectOptions
{
    Role = PersistentPtyRole.Controller,
});

Console.WriteLine($"attached as {attached.Attachment.Role}, replay ends at {attached.Attachment.Replay.EndOffset}");

await attached.WriteAsync(Encoding.UTF8.GetBytes("echo hello\n"), cancellationToken);
await attached.ResizeAsync(cols: 100, rows: 30, cancellationToken);

await foreach (var frame in attached.ReadAsync(cancellationToken))
{
    switch (frame)
    {
        case PersistentPtyOutputFrame output:
            await sink.WriteAsync(output.Data, cancellationToken);
            break;
        case PersistentPtyResizedFrame resized:
            Console.WriteLine($"server resized to {resized.Cols}x{resized.Rows}");
            break;
        case PersistentPtyExitedFrame exited:
            Console.WriteLine($"exited with {exited.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}");
            return;
        case PersistentPtyUnknownFrame unknown:
            Console.WriteLine($"unknown frame {unknown.Type}");
            break;
    }
}
```

What is different here:

- **`ConnectAsync` returns only after the server's attach**, so `Attachment` is always populated:
  the attachment id, the terminal as it stood, the replay bounds, the resize generation, and the
  **granted** role.
- **The role you asked for is not necessarily the role you got.** Ask for `Controller`, read
  `attached.Attachment.Role`, and believe the answer — input written from an observer connection is
  accepted by the SDK and dropped by the server. Set `Takeover = true` to take control from the
  current controller; without it a second controller is attached as an observer instead. Reusing a
  previous connection's `AttachmentId` is how a reconnect reclaims its own control.
- **Output is bytes, never decoded.** `PersistentPtyOutputFrame.Data` is `ReadOnlyMemory<byte>`,
  because a frame is free to split a multi-byte character in half. Feed the bytes to a terminal
  emulator, or decode incrementally with a stateful `Decoder` — never with a fresh
  `Encoding.UTF8.GetString` per frame.
- **The frame family is richer**: attached, output, replay-complete, resized, exited,
  controller-changed, title-changed, foreground-process-changed — plus `PersistentPtyUnknownFrame`,
  which carries a control type this build does not know together with its raw JSON rather than
  failing your read. This socket is an experimental upstream surface and may grow frame kinds;
  that carrier is why a newer daemon will not break your loop.
- **Resuming uses offsets, not the `-1` trick.** `Cursor` is `null` or `0` for "from the oldest
  retained byte" — there is no live-only mode. To continue where you left off, anchor on the
  previous connection's replay-complete `EndOffset` or on the terminal's `Info.Output.Tail`. A
  cursor pointing at output that has been trimmed is advanced by the server, which reports the gap
  in the attachment's replay bounds.

The collection client also carries the daemon's lifecycle doors: `HandoffAsync` prepares the daemon
to outlive this server until a replacement claims it, and `ShutdownAsync` stops the daemon **and
every terminal it owns** — not just one.

## 🪟 The Windows platform note

**Persistent PTYs do not work on Windows hosts.** At the pinned snapshot, upstream's `opencode-pty`
daemon ships `darwin` and `linux` platform packages only — that is the whole root cause.

`create` is the one route that starts the daemon, so on Windows it answers the API's declared
**HTTP 503** with a `ServiceUnavailableError` whose `Service` is `opencode-pty`. Every other route
takes its daemon-absent arm rather than erroring: `list` returns an empty list, `read` a null
payload, the id-keyed reads and writes a 404 they share with an unknown id, `shutdown` a 204, and
`connect` closes with 4404.

That is why the create snippet above uses `NoThrow` and checks for exactly that shape. Match the
**status and the service name together** — a bare 503 could be anything, and a 400 or 401 means
your request or credential was wrong, not that the daemon is missing.

Normal PTYs are unaffected and work on all supported platforms.

## 🎫 Connect tokens

Both families expose `CreateConnectTokenAsync`, which mints a short-lived single-use ticket for
handing a connection to a **browser**; the SDK's own `ConnectAsync` never uses one, because it
upgrades with the Basic credential it already holds — a header beats a single-use secret in a URL
that lands in logs.

```csharp
var token = await pty.CreateConnectTokenAsync();

Console.WriteLine($"ticket expires in {token.ConnectToken.ExpiresIn}");
```
