# Modern .NET allocation APIs for the runtime pipeline rebuild

Date: 2026-08-24

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`.

This survey maps the modern .NET low-allocation APIs against the locked product matrix
(`netstandard2.0;net472;net8.0;net9.0;net10.0`) and against the planned pipeline increments:
increment 3 (pooled response-body buffering with progress timeout), increment 4
(`ResponseEncodingPolicy` hardening with `ReadAsStringAsync` parity), and SSE stage 2 (byte-level
UTF-8 frame scanning). Every finding is claim → primary source → TFM matrix → gate tag. Downlevel
package facts were verified against the artifacts this repository actually restores: Polyfill
11.0.2 `contentFiles` source, the `System.Memory` 4.6.3 assemblies (public-type enumeration via
metadata reader), `Microsoft.Bcl.Memory` 10.0.11 (type/method enumeration), and the SDK project's
`project.assets.json`. Nothing here changes canon; dependency additions named below are decisions
for the maintainer, not decisions made by this document.

Baseline facts about the current dependency graph (verified in
`src/OpenCode.Sdk/obj/project.assets.json`):

| Package | net472 / ns2.0 | net8.0 | net9.0 | net10.0 |
|---|---|---|---|---|
| `System.Memory` 4.6.3 | resolved (portable span) | inbox | inbox | inbox |
| `System.Buffers` 4.6.1 (`ArrayPool<T>`) | resolved | inbox | inbox | inbox |
| `System.IO.Pipelines` 10.0.11 | resolved (via `System.Text.Json`) | resolved | resolved | inbox |
| `System.Text.Json` 10.0.11 | resolved | resolved | resolved | resolved |

The net10.0 leg carries no `System.IO.Pipelines` package because modern .NET includes it in the
shared framework ([Pipelines overview](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines):
"In modern .NET versions, `System.IO.Pipelines` is included in the shared framework and doesn't
require a separate NuGet package"; confirmed by the empty `net10.0` dependency group in the
`System.Text.Json` 10.0.11 nuspec).

## 1. Charset, BOM, and strict UTF-8 without exceptions or substrings

### `SearchValues<T>` (byte and char)

- Claim: immutable precomputed value set for repeated `IndexOfAny`-style searching; the search
  strategy (bitmaps, vectorized probes) is chosen once at `SearchValues.Create` and instances are
  meant to be cached in `static readonly` fields.
- Source: [SearchValues&lt;T&gt; API docs](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.searchvalues-1)
  (moniker range `net-8.0`+ only, assembly `System.Runtime.dll`);
  [Performance Improvements in .NET 8](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/)
  (SearchValues section).
- TFM matrix: net10/9/8 native. net472/ns2.0: **no coverage** — not in `System.Memory` 4.6.3
  (verified public-type list), not in `Microsoft.Bcl.Memory` 10.0.11 (verified type list:
  `Index`, `Range`, `Utf8`, `Base64Url` only), not in Polyfill 11.0.2 (grep over
  `contentFiles/cs/netstandard2.0` and `net472`: zero hits). Any use is `#if NET`-guarded or a
  per-TFM adapter.
- Gate: none for increment 4 (charset/BOM detection compares fixed 2–4-byte prefixes; there is no
  repeated multi-value scan). Marginal for SSE stage 2 — see the `IndexOfAny` finding: a
  two-value CR/LF scan does not need `SearchValues`, and dotnet/runtime's own SSE parser does not
  use it.

### `MemoryExtensions.IndexOfAny` over bytes and chars

- Claim: `span.IndexOfAny(a, b)` (two/three-value overloads) is the right primitive for CR/LF
  scanning; it is vectorized on modern runtimes and merely scalar downlevel.
- Source: `MemoryExtensions` is exported by `System.Memory` 4.6.3 `lib/net462` and
  `lib/netstandard2.0` (verified by assembly metadata enumeration). Downlevel performance: Stephen
  Toub, [All About Span (MSDN Magazine, Jan 2018)](https://learn.microsoft.com/en-us/archive/msdn-magazine/2018/january/csharp-all-about-span-exploring-a-new-net-mainstay)
  — the `System.Memory` package serves older frameworks "albeit without some of the optimizations
  implemented when built into to the platform"; the fast span depends on JIT intrinsics
  (`ref T` field, bounds-check elision) that .NET Framework's JIT does not provide.
- TFM matrix: all five targets compile the same call; net10/9/8 get vectorized intrinsics,
  net472/ns2.0 get the portable scalar implementation.
- Gate: sse-stage-2 (byte-level CR/LF scan). Also already used by the committed
  `ServerSentEventReader` for char scanning. Downlevel benchmark evidence must come from the
  net472 leg per `docs/engineering/quality-gates.md`; the modern-.NET speedup does not transfer.

### `Utf8.IsValid(ReadOnlySpan<byte>)` (System.Text.Unicode)

- Claim: exception-free strict UTF-8 validation — exactly the check `ResponseEncodingPolicy`
  currently emulates by round-tripping `GetCharCount` through a throwing decoder and catching
  `DecoderFallbackException`, and the check a byte-level SSE reader needs to preserve the "throw
  on malformed UTF-8" contract without a decoder.
- Source: [Utf8.IsValid API docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.unicode.utf8.isvalid)
  — monikers `net-8.0`+ **plus** `netstandard-2.0-pp`/`netframework-4.6.2-pp` via
  `Microsoft.Bcl.Memory.dll`. Verified concretely: `Microsoft.Bcl.Memory` 10.0.11
  `lib/netstandard2.0` exports `System.Text.Unicode.Utf8` with `IsValid`, `FromUtf16`, `ToUtf16`
  (metadata enumeration of the downloaded package).
  [Microsoft.Bcl.Memory on NuGet](https://www.nuget.org/packages/Microsoft.Bcl.Memory): stable
  since the 9.0 wave, targets ns2.0/net462+, downlevel deps are `System.Memory` ≥ 4.6.3 (already
  in the graph), `System.Runtime.CompilerServices.Unsafe`, `System.ValueTuple`.
- TFM matrix: net10/9/8 native (`System.Runtime`). net472/ns2.0: available only by **adding the
  `Microsoft.Bcl.Memory` package** — a new dependency decision; Polyfill does not carry it.
  Downlevel the implementation compiles against portable spans (correctness parity, scalar speed).
- Gate: increment-4 (replaces `DecoderFallbackException` control flow in `IsValidUtf8`) and
  sse-stage-2 (validate `data` payload bytes that are no longer decoded). If the dependency is
  declined, the no-new-package alternatives are (a) keeping the strict-decoder try/catch on
  downlevel behind the existing per-TFM adapter rule, or (b) a hand-written scalar UTF-8 DFA scan.

### `Encoding.GetString(ReadOnlySpan<byte>)`

- Claim: decodes a span without materializing an intermediate `byte[]` — the natural sink for a
  pooled buffer's span on the non-UTF-8 decode path.
- Source: [Encoding.GetString API docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getstring)
  — the span overload's moniker range is `netcore-2.1`+/`netstandard-2.1`; the
  `(byte[], int, int)` overload covers every framework.
- TFM matrix: net10/9/8 native. net472/ns2.0: Polyfill 11.0.2 supplies the shape
  (`Polyfill_Encoding.cs`), but with this repository's `AllowUnsafeBlocks` unset the compiled
  fallback is `target.GetString(bytes.ToArray())` (verified in the package source) — a full copy
  of the payload, i.e. **worse** than calling `GetString(byte[], int, int)` on the backing array
  directly. On downlevel the pooled buffer should hand its `(array, offset, count)` triple to the
  array overload; the span overload is a net8+ nicety, not a downlevel win.
- Gate: increment-4 (non-UTF-8 decode of the pooled buffer without `ToArray`).

### `Encoding.GetChars`/`TryGetChars` span overloads

- Claim: `TryGetChars(ReadOnlySpan<byte>, Span<char>, out int)` gives exception-free bounded
  decoding into caller-owned (poolable) char buffers.
- Source: [Encoding.TryGetChars API docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.trygetchars)
  — moniker range `net-8.0`+ only. Polyfill 11.0.2 `Polyfill_Encoding_TryGet.cs` implements it
  downlevel as `GetCharCount(bytes)` + `GetChars(bytes, chars)` (verified source), and without
  unsafe both of those helpers allocate temporary arrays (`bytes.ToArray()`, `new char[...]`,
  verified in `Polyfill_Encoding_GetChars.cs`).
- TFM matrix: net10/9/8 native and allocation-free. net472/ns2.0: Polyfill shape only — double
  pass plus temp arrays; not a downlevel optimization, only source compatibility.
- Gate: increment-4, and only on the `#if NET` side; downlevel keeps `Decoder`/array APIs.

### `Encoding.GetEncoding` has no span overload — the `charset[1..^1]` allocation

- Claim: the quoted-charset unquote cannot be made fully allocation-free by spans alone, because
  `Encoding.GetEncoding` accepts only `Int32` or `String` (no `ReadOnlySpan<char>` overload as of
  .NET 10), and invalid names throw `ArgumentException`/`NotSupportedException`.
- Source: [Encoding.GetEncoding API docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getencoding)
  (overload list verified: `GetEncoding(Int32)`, `GetEncoding(String)`, plus fallback variants).
  Parity semantics: dotnet/runtime `HttpContent.ReadBufferAsString`
  ([release/9.0 source](https://github.com/dotnet/runtime/blob/release/9.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpContent.cs))
  — "Remove at most a single set of quotes" via `Substring`, and it too allocates the unquoted
  string; invalid charset: `catch (ArgumentException e) { throw new InvalidOperationException(...) }`.
- Analysis (not sourced): the allocation is reachable only when the server sends a *quoted*
  charset, and `HttpContent` itself pays it, so parity does not require removing it. An
  allocation-free fast path is possible by comparing the unquoted span ordinally-ignore-case
  against well-known names (`utf-8`, `us-ascii`, `utf-16`, …) before falling back to
  `GetEncoding(string)`; that is an optimization choice, not a parity requirement. Note the
  runtime wraps only `ArgumentException`; the committed policy also catches
  `NotSupportedException`, which is the documented throw for a valid-but-unsupported codepage —
  a defensible superset, worth an explicit test comment rather than silent divergence.
- Gate: increment-4.

### `System.Text.Rune`

- Claim: non-throwing per-scalar UTF-8 decode (`Rune.DecodeFromUtf8` → `OperationStatus`), useful
  only if the SDK ever needs scalar-by-scalar iteration; whole-buffer validity is `Utf8.IsValid`'s
  job.
- Source: [Rune API docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.rune) — moniker
  range `netcore-3.0`+ only; **not** in netstandard 2.1, not in any .NET Framework, no Microsoft
  backport package (absent from `Microsoft.Bcl.Memory` type list), no Polyfill coverage (verified
  grep).
- TFM matrix: net10/9/8 native; net472/ns2.0 unavailable, full stop.
- Gate: none. Nothing in increments 3/4 or SSE stage 2 needs per-scalar iteration; if a future
  need appears it is a per-TFM adapter by house rule.

## 2. Pooled response-body buffering (increment 3)

### `ArrayPool<byte>` discipline

- Claim: `Rent(minimum)` may return a larger array (callers track logical length separately);
  `Return` without `clearArray` leaves contents visible to the next renter; returning twice or
  using after return is classified by Microsoft as a high-severity double-free/use-after-free
  security issue; the pool is thread-safe.
- Source: [ArrayPool&lt;T&gt; docs](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)
  and [Return docs](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return)
  ("Returning the same array reference twice or continuing to use the array reference after it has
  been returned is a high-severity security issue…"; `clearArray` clears only when the pool keeps
  the buffer).
- TFM matrix: net10/9/8 inbox (`System.Private.CoreLib` TLS-over-per-core-stacks pool);
  net472/ns2.0 via `System.Buffers` 4.6.1 already in the graph (bucketed `DefaultArrayPool`).
- Gate: increment-3. Design consequences: single-owner buffer type with an idempotent
  dispose (the dirty-worktree experiment's shape); decide explicitly whether response bodies
  (which can carry credentials) warrant `clearArray: true` on return — dotnet/runtime's own
  `LimitArrayPoolWriteStream` does **not** clear (see below), so clearing is a deliberate
  hardening beyond upstream, at a measurable memset cost on large bodies.

### `ArrayBufferWriter<T>` and `IBufferWriter<byte>`

- Claim: `ArrayBufferWriter<T>` is a contiguous grow-and-copy sink but is **unpooled** ("heap-based,
  array-backed"; growth allocates fresh arrays) and does not exist downlevel;
  `IBufferWriter<byte>` (the abstraction) is available everywhere.
- Source: [ArrayBufferWriter&lt;T&gt; docs](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraybufferwriter-1)
  — moniker range `netstandard-2.1`/`netcore-3.0`+; verified absent from `System.Memory` 4.6.3
  netstandard2.0/net462 assemblies. `IBufferWriter<T>` verified present in both 4.6.3 assemblies.
- TFM matrix: `ArrayBufferWriter<T>`: net10/9/8 only. `IBufferWriter<byte>`: all five.
- Gate: increment-3 — rules `ArrayBufferWriter` out twice over (no pooling, no downlevel);
  a hand-written ArrayPool-backed growable buffer is required. Implementing `IBufferWriter<byte>`
  on it is optional; nothing in the pipeline needs the interface today.

### The reference design: dotnet/runtime's own response buffering, per release

- Claim: dotnet/runtime itself moved from unpooled to pooled contiguous buffering between .NET 9
  and .NET 10, and the .NET 10 implementation is a direct blueprint for increment 3:
  - net8/net9: `HttpContent` buffers into `LimitMemoryStream : MemoryStream` — capacity primed
    from `Content-Length` when present (after checking it against `maxBufferSize`), else 0;
    ordinary unpooled `MemoryStream` doubling; a max-size check on every write throws
    `HttpRequestException` on overflow. `ReadAsByteArrayAsync` then copies out with
    `_bufferedContent.ToArray()`.
  - net10: `_bufferedContent` is `LimitArrayPoolWriteStream` — rents from
    `ArrayPool<byte>.Shared`, initial rent primed from `Content-Length` clamped by
    `MaxInitialBufferSize = 16 MB` (`MinInitialBufferSize = 16 KB` when length is unknown), grows
    by rent-double/copy/return (`ResizeFactor = 2`, `LastResizeFactor = 4`), enforces the max on
    every write, returns the buffer on dispose, and `ReadAsByteArrayAsync` escapes via
    `CreateCopy()` — one exact-size allocation because the array leaves the library.
  - net472: .NET Framework's `System.Net.Http` has its own unpooled `LimitMemoryStream`
    (referencesource `System/net/System/Net/Http/HttpContent.cs`).
- Source: [release/9.0 HttpContent.cs](https://github.com/dotnet/runtime/blob/release/9.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpContent.cs)
  (`CreateMemoryStream` → `LimitMemoryStream`, `ReadBufferedContentAsByteArray` → `ToArray()`),
  [release/10.0 HttpContent.cs](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Net.Http/src/System/Net/Http/HttpContent.cs)
  (`private LimitArrayPoolWriteStream? _bufferedContent`, `CreateCopy`), current
  [main HttpContent.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/HttpContent.cs)
  (same pooled design, 16 KB/16 MB constants),
  [microsoft/referencesource HttpContent.cs](https://github.com/microsoft/referencesource/blob/master/System/net/System/Net/Http/HttpContent.cs).
- TFM consequence for the SDK: `ReadAsByteArrayAsync` produces at minimum one exact-size escaping
  `byte[]` on every TFM, **plus** unpooled geometric growth garbage on net8/net9/net472 —
  precisely the two costs a `ResponseBufferingPolicy` that owns its pooled buffer and hands out
  `ReadOnlyMemory<byte>` (never an escaping array) eliminates. Trusting `Content-Length` only as a
  clamped initial-capacity *hint* (16 MB cap) while enforcing an absolute maximum on write is the
  upstream-proven answer to absent or lying lengths.
- Gate: increment-3.

### Receiving bytes: `HttpContent.CopyToAsync` vs `ReadAsStreamAsync`

- Claim: on unbuffered content, `CopyToAsync` invokes `SerializeToStreamAsync(destination, …)`
  directly — the handler's response content writes straight into the caller's sink with no
  intermediate whole-body buffer — and wraps `IOException`/`ObjectDisposedException` into
  `HttpRequestException`. This makes `CopyToAsync` into the pooled buffer the minimal-copy intake
  on every TFM, versus `ReadAsStreamAsync` + manual read loop (equivalent copies, more code) or
  `ReadAsByteArrayAsync` (extra escaping array).
- Source: release/9.0 and release/10.0 `HttpContent.cs` (`InternalCopyToAsync`,
  `StreamCopyExceptionNeedsWrapping`); same shape in referencesource `HttpContent.cs` for net472.
- Also load-bearing: `LoadIntoBuffer`'s internal cancellation pattern —
  `cancellationToken.Register(static s => ((HttpContent)s!).Dispose(), this)` with the comment
  that tearing down the underlying stream on cancellation/timeout is acceptable because the
  content has not been handed to users yet. That is dotnet/runtime's own precedent for
  dispose-to-interrupt during buffering (see §4).
- Gate: increment-3.

### Segmented alternatives: RecyclableMemoryStream, Pipe, `ReadOnlySequence<byte>`

- Claim: segmented pooled buffers avoid grow-and-copy entirely but push segment handling onto
  every consumer.
  - `Microsoft.IO.RecyclableMemoryStream`: block-chained small-pool plus large-pool design,
    eliminates LOH allocations and gen-2 pressure; targets net462/ns2.0+; streams are not
    thread-safe; guidance is to avoid `GetBuffer()` (forces a contiguous large buffer) in favor of
    `GetReadOnlySequence()`; pools must be capped (`MaximumFreeSmallPoolBytes` etc.) or they grow
    unbounded. A new package dependency.
  - `Pipe`/`PipeWriter` (see §3) is also a segmented pooled buffer producing
    `ReadOnlySequence<byte>`.
  - Consuming a `ReadOnlySequence<byte>` downlevel is manual: `SequenceReader<T>` is
    `netstandard2.1`/`netcore3.0`+ only ([SequenceReader docs](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.sequencereader-1))
    and verified absent from `System.Memory` 4.6.3; only `ReadOnlySequence<T>` itself and
    `BuffersExtensions` ship downlevel. `Utf8JsonReader` does accept `ReadOnlySequence<byte>` on
    all TFMs, so JSON materialization tolerates segmentation, but `ResponseEncodingPolicy`'s BOM
    sniff, UTF-8 validation, and `EncodedResponseBody.Utf8Body : ReadOnlyMemory<byte>` contract
    all want contiguous memory.
- Source: [microsoft/Microsoft.IO.RecyclableMemoryStream README](https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream);
  SequenceReader docs as above; `System.Memory` 4.6.3 assembly enumeration.
- Comparison point: `System.ClientModel` (the Azure/OpenAI client base) buffers responses with a
  plain `new MemoryStream()` subclass and `Stream.CopyTo(Async)`, unpooled
  ([HttpClientPipelineTransport.Response.cs](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/System.ClientModel/src/Pipeline/HttpClientPipelineTransport.Response.cs))
  — i.e. the pooled contiguous design already outperforms what that library ships.
- Gate: increment-3 — evidence favors the contiguous ArrayPool grow-and-copy design (matches
  dotnet/runtime net10, keeps `ReadOnlyMemory<byte>` contracts, zero new dependencies, no
  downlevel `SequenceReader` gap). RecyclableMemoryStream is the fallback if profile evidence ever
  shows grow-and-copy of very large bodies dominating; it is not needed to start.

## 3. SSE framing: byte-level scanning vs decode-then-scan (SSE stage 2)

### dotnet/runtime already ships a byte-level SSE parser that runs on every SDK target

- Claim: `System.Net.ServerSentEvents` (`SseParser`, `SseParser<T>`, `SseItem<T>`) is a
  first-party byte-level SSE parser: ArrayPool-rented line buffer (1024 B initial, doubling,
  1 GB hard cap, buffers returned in `finally`), scans raw bytes with
  `IndexOfAny((byte)'\r', (byte)'\n')`, tracks a `_lastSearchedForNewline` index so refills don't
  rescan, defers a CR at buffer end until the next read to pair CRLF across chunk boundaries,
  skips one leading UTF-8 BOM, decodes **only** `event`/`id` field values to UTF-16
  (`Encoding.UTF8.GetString`), and hands the `data` payload to the caller as
  `ReadOnlySpan<byte>` via `SseItemParser<T>(string eventType, ReadOnlySpan<byte> data)` — for a
  JSON SDK that span can feed `Utf8JsonReader` with no UTF-16 round trip at all.
- Source: [SseParser_1.cs in dotnet/runtime](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.ServerSentEvents/src/System/Net/ServerSentEvents/SseParser_1.cs);
  [System.Net.ServerSentEvents on NuGet](https://www.nuget.org/packages/System.Net.ServerSentEvents)
  — 10.0.11 targets ns2.0/net462/net8/net9/net10; downlevel deps (`System.Memory` 4.6.3,
  `Microsoft.Bcl.AsyncInterfaces`, `System.Threading.Tasks.Extensions`) are already satisfied by
  the SDK's existing graph.
- Multi-byte boundary safety: scanning UTF-8 bytes for CR/LF cannot produce false positives —
  0x0D/0x0A are ASCII and UTF-8 continuation bytes are always ≥ 0x80, so a delimiter byte can
  never occur inside a multi-byte character. dotnet/runtime relies on exactly this (no boundary
  bookkeeping in `SseParser`); the same argument covers the SDK's `:` and space field-syntax
  bytes.
- Behavioral delta the SDK must decide: the committed `ServerSentEventReader` **throws** on
  malformed UTF-8 (strict decoder) and on a body cut mid-line; `SseParser` never validates the
  `data` bytes (and decodes names with replacement semantics). A byte-level stage-2 reader keeps
  the strict contract by running `Utf8.IsValid` over each emitted payload (§1) — a separate
  linear pass, still exception-free and cheaper than today's full decode — or the contract is
  consciously relaxed to replacement parity. Either way the choice is explicit, tested, and owned
  by the SDK, not inherited silently.
- Gate: sse-stage-2 — adopt the package or mirror its mechanics (pooled contiguous line buffer +
  byte `IndexOfAny` + CR-deferral + BOM skip). Mirroring keeps zero new dependencies and the
  SDK's own exception taxonomy/frame-size ceiling; adopting outsources framing but not the strict
  UTF-8 or size-limit policy. Both beat decode-to-UTF-16-then-scan on allocation: today's path
  decodes every payload byte to chars (2× width) into a `StringBuilder` before `ToString`.

### `PipeReader`/`System.IO.Pipelines` vs plain Stream reads

- Claim: `PipeReader` solves exactly the SSE framing chores — buffer growth for partial lines,
  compaction, pooling — via `ReadAsync` + `AdvanceTo(consumed, examined)`, with buffers owned by
  the pipe and surfaced as `ReadOnlySequence<byte>`; `PipeReader.Create(stream)` adapts an HTTP
  response stream (`StreamPipeReaderOptions`: `BufferSize` default 4096, `MinimumReadSize`,
  `LeaveOpen`); `CancelPendingRead()` interrupts a pending read non-exceptionally. It is
  available on all five targets at zero added cost (table above).
- Source: [Pipelines overview](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines)
  (contract, options, common-problem catalogue: consumed/examined mistakes cause data loss,
  hangs, or unbounded buffering; sequences must not be touched after `AdvanceTo`).
- Counter-evidence: dotnet/runtime chose **not** to build `SseParser` on `PipeReader` — a single
  contiguous ArrayPool line buffer over `Stream.ReadAsync` is sufficient for one-directional
  line-framed parsing, avoids the consumed/examined footgun class, and avoids downlevel
  `ReadOnlySequence` handling without `SequenceReader` (§2). The pipe's strengths (backpressure,
  separate fill/parse loops, multi-segment zero-copy) buy nothing for a single-reader SSE loop.
- Gate: sse-stage-2 — available if wanted, but the primary-source precedent says the simpler
  SseParser-style buffer is the better fit; note `Stream.ReadAsync(Memory<byte>)` downlevel via
  Polyfill wraps the array-based overload through `MemoryMarshal.TryGetArray` (verified package
  source), so reads into a pooled array are copy-free on every TFM.

### Where `Utf8JsonReader`-style byte processing fits

- Claim: the SDK's materialization layer (`System.Text.Json` 10.0.11 on all five TFMs) already
  consumes UTF-8 bytes directly; keeping SSE payloads as bytes end-to-end
  (`ReadOnlySpan<byte>`/`ReadOnlyMemory<byte>` → `JsonSerializer.Deserialize` UTF-8 overloads /
  `Utf8JsonReader`) removes the UTF-16 detour entirely, mirroring
  `EncodedResponseBody.Utf8Body`'s existing design for non-streaming bodies.
- Source: [Utf8JsonReader API docs](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader)
  (ref struct over `ReadOnlySpan<byte>`/`ReadOnlySequence<byte>`); package presence per TFM
  verified in `project.assets.json`.
- Gate: sse-stage-2.

## 4. Progress-timeout machinery (increment 3)

### `CancelAfter` re-arm — the downlevel-safe timer primitive

- Claim: `CancelAfter` is documented to *reset* the delay when called again before expiry, and on
  every target it lazily creates exactly one timer and re-arms it with `Timer.Change` — so a
  per-progress re-arm (bump the deadline after each successful read) is allocation-free after the
  first call, on net472 included.
- Source: [CancelAfter API docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelafter)
  ("Subsequent calls to CancelAfter will reset the `millisecondsDelay`…"; moniker range includes
  netframework-4.5+ and netstandard2.0); modern implementation
  [CancellationTokenSource.cs (dotnet/runtime main)](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs)
  (lazy `TimerQueueTimer`, `timer.Change` on re-arm); .NET Framework implementation
  [referencesource CancellationTokenSource.cs](https://github.com/microsoft/referencesource/blob/master/mscorlib/system/threading/CancellationTokenSource.cs)
  (lazy `new Timer(...)` under `Interlocked.CompareExchange`, then `m_timer.Change(delay, -1)`).
- TFM matrix: all five, same re-arm cost model.
- Gate: increment-3 — this is the core of the progress timeout: one linked CTS per operation,
  `CancelAfter(progressTimeout)` re-armed on every observed read.

### `TryReset` — modern-only, and not needed

- Claim: `TryReset` (reuse one CTS across unrelated operations, Kestrel-style) is .NET 6+ only,
  has no Polyfill (the package's only CTS member downlevel is a spin-waiting `CancelAsync` shim —
  verified source), and its remarks confine it to a sole owner with no concurrent cancel; since
  the pipeline matrix includes net472/ns2.0, cross-operation CTS reuse cannot be the shared
  design, and per-operation linked CTS + `CancelAfter` re-arm covers the requirement everywhere.
- Source: [TryReset API docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.tryreset)
  (moniker range `net-6.0`+; remarks on erroneous remaining registrations and non-thread-safe
  concurrent use); Polyfill 11.0.2 `Polyfill_CancellationTokenSource.cs` (verified).
- Gate: increment-3 — decision input: do **not** build the design around `TryReset`; if a
  modern-only fast path is ever measured to matter it is a per-TFM adapter, but the re-arm
  pattern makes it unnecessary.

### Linked token sources and registration costs: per operation, not per read

- Claim: linking is cheap-but-not-free and should happen once per operation. Modern runtime:
  `CreateLinkedTokenSource` with one/two tokens uses specialized
  `Linked1CancellationTokenSource`/`Linked2CancellationTokenSource` (a CTS holding
  `CancellationTokenRegistration` fields, shared static callback), and `Register` reuses pooled
  `CallbackNode` objects from a per-source free list. .NET Framework: a linked CTS allocates the
  CTS plus a `CancellationTokenRegistration[]` and registers into `SparselyPopulatedArray`
  fragments with no pooling — per-read linking would multiply allocations exactly where the
  runtime is weakest.
- Source: [dotnet/runtime CancellationTokenSource.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs)
  (Linked1/Linked2/LinkedN, `Registrations.FreeNodeList`);
  [referencesource CancellationTokenSource.cs](https://github.com/microsoft/referencesource/blob/master/mscorlib/system/threading/CancellationTokenSource.cs)
  (`CreateLinkedTokenSource` allocating `m_linkingRegistrations`, `SparselyPopulatedArray`).
- Precedent for the whole shape: `HttpClient` itself implements its timeout as
  `CreateLinkedTokenSource(callerToken, pendingRequestsCts.Token)` + `cts.CancelAfter(_timeout)`,
  created only when a timeout or cancelable token exists, disposed in `FinishSend`, and
  distinguishes timeout from caller cancel by "linked CTS canceled but caller token not" →
  `TaskCanceledException` wrapping an inner `TimeoutException`
  ([HttpClient.cs, dotnet/runtime main](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/HttpClient.cs)).
  The buffering policy's timeout classification should mirror this discrimination pattern.
- Gate: increment-3.

### Dispose-to-interrupt reliability: SocketsHttpHandler vs net472 ConnectStream

- Claim (modern .NET): SocketsHttpHandler response streams honor the token *during* a read —
  `ContentLengthReadStream.ReadAsync` registers via `_connection.RegisterCancellation(token)` and
  surfaces `OperationCanceledException` through `CancellationHelper`; so on net8/9/10 the linked
  token passed to each read is the primary interrupt and disposal is cleanup, not the mechanism.
  Disposing an incompletely-read response stream drains up to `MaxResponseDrainSize`/time and
  otherwise disposes the underlying connection.
- Source: [ContentLengthReadStream.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/ContentLengthReadStream.cs),
  [HttpContentReadStream.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/HttpContentReadStream.cs)
  (`NeedsDrain`/`DrainOnDisposeAsync`).
- Claim (net472): the token is dead weight once a read is in flight. `ConnectStream` (the
  `HttpWebResponse` body stream behind `HttpClientHandler`) does not override the Task-based
  `ReadAsync` — its only async surface is APM `BeginRead`/`EndRead` — so reads fall through to
  `Stream.ReadAsync`, whose .NET Framework implementation is literally:
  `return cancellationToken.IsCancellationRequested ? Task.FromCancellation<int>(cancellationToken) : BeginEndReadAsync(buffer, offset, count);`
  — a pre-check only, no in-flight registration. Interrupting a hung read therefore requires the
  teardown path: `ConnectStream.CloseInternal(aborting: true)` → `m_Connection.AbortSocket(true)`
  fails the pending socket read (surfacing `WebException`/`IOException`/`ObjectDisposedException`),
  whereas a *normal* close attempts `DrainSocket()` — reading the rest of the body, which can
  itself block on a stalled server. So the downlevel progress timeout must abort/dispose (response
  message or content) to interrupt and must classify the resulting exception as its own timeout,
  and it must not expect a graceful `Close` to unblock anything.
- Source: [referencesource mscorlib/system/io/stream.cs](https://github.com/microsoft/referencesource/blob/master/mscorlib/system/io/stream.cs)
  (quoted `ReadAsync`), [referencesource System/net/System/Net/_ConnectStream.cs](https://github.com/microsoft/referencesource/blob/master/System/net/System/Net/_ConnectStream.cs)
  (`CloseInternal(bool internalCall, bool aborting)`, `AbortSocket`, `DrainSocket`).
- Registration allocation: modern `token.Register` reuses pooled `CallbackNode`s; net472
  allocates registration state per call (`SparselyPopulatedArray` inserts) — one more reason the
  interrupt registration (e.g. "on cancel, dispose the content") belongs at operation scope, the
  way `HttpContent.LoadIntoBuffer` registers a single static-lambda dispose callback for the
  whole buffering operation (§2).
- Gate: increment-3. Matches and explains the committed `ResponseBodyReader` behavior
  (`WaitAsync` + content dispose on timeout) and commit `713f09a` ("cancel downlevel stream
  reads"): those were workarounds for exactly this net472 property; the rebuilt policy keeps
  dispose-to-interrupt as the downlevel mechanism and token-per-read as the modern mechanism —
  algorithm divergence, hence a per-TFM adapter seam by house rule.

## 5. Other load-bearing findings

### `ReadAsStringAsync` parity semantics, quotable

- For increment 4's KEEP contract, the authoritative behavior list from
  `HttpContent.ReadBufferAsString` (release/9.0 and release/10.0, identical semantics): charset
  wins over BOM; at most one set of surrounding quotes stripped; `Encoding.GetEncoding` failure →
  `ArgumentException` wrapped in `InvalidOperationException`; no charset → BOM sniff in order
  UTF-8 (EF BB BF), UTF-32 LE (FF FE 00 00, checked before UTF-16 LE by 4-byte disambiguation),
  UTF-16 LE (FF FE), UTF-16 BE (FE FF); default UTF-8 with `bomLength = 0`; BOM dropped before
  decode; decode via the encoding's default (replacement) fallback, never strict. Empty body
  short-circuits to `""` before touching the charset (matches the committed policy's comment).
  Source links in §2. Gate: increment-4 — the committed `ResponseEncodingPolicy` already mirrors
  this; the rebuild only swaps the *mechanics* (Utf8.IsValid, span decode) under an unchanged
  observable contract.

### `ValueTask` discipline

- `ValueTask`/`ValueTask<T>` exist to avoid `Task` allocation when operations complete
  synchronously; `IValueTaskSource` extends that to pooled asynchronous completions; a
  `ValueTask` must be awaited exactly once and never concurrently; default to `Task` on public
  surfaces unless the allocation is measured to matter. For the internal pipeline: modern
  `Stream.ReadAsync(Memory<byte>)` returns `ValueTask<int>` natively; the Polyfill downlevel
  shim returns `new ValueTask<int>(task)` (wraps the array-overload Task — shape, not savings).
  Source: Stephen Toub,
  [Understanding the Whys, Whats, and Whens of ValueTask](https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/);
  Polyfill 11.0.2 `Polyfill_Stream_Read.cs` (verified). Gate: none (general discipline for
  increment-3 internals; keep hot internal seams `ValueTask`-shaped on `#if NET`, don't contort
  downlevel).

### Downlevel span performance is a different regime

- Every span-based win above shrinks on net472/ns2.0: the portable `System.Memory` span lacks the
  JIT intrinsics, bounds-check elision, and vectorized `IndexOfAny` of the inbox implementation
  (Toub, All About Span; the package ships the functionality "without some of the
  optimizations"). Allocation savings (no `byte[]`/`string`/UTF-16 intermediates) transfer to
  net472; throughput claims do not, and per `docs/engineering/quality-gates.md` only the Windows
  net472 benchmark leg can substantiate them. Gate: cross-cutting evidence rule for all three
  increments.

## Recommendations by increment

| Increment | Use | Natively | Downlevel (net472/ns2.0) | Avoid / defer |
|---|---|---|---|---|
| increment-3 buffering | Hand-written ArrayPool-backed contiguous grow-and-copy buffer fed by `HttpContent.CopyToAsync`; `Content-Length` as clamped initial-rent hint (16 MB-style cap) + absolute max enforced on write; expose `ReadOnlyMemory<byte>`, never an escaping array (goes one step beyond net10's `CreateCopy`) | mirrors net10 `LimitArrayPoolWriteStream` | `System.Buffers` 4.6.1 + `System.Memory` 4.6.3 already present; same algorithm | `ArrayBufferWriter` (unpooled, no downlevel); `Pipe`/RecyclableMemoryStream segmentation (no `SequenceReader` downlevel, contiguous contracts); pooled-buffer `clearArray` is an explicit security-vs-cost decision |
| increment-3 progress timeout | One linked CTS per operation (`HttpClient` precedent), `CancelAfter` re-armed per observed read (documented reset semantics; single reused timer on all TFMs); timeout-vs-caller discrimination via linked-vs-caller token state; single operation-scoped cancel registration that disposes content (runtime's `LoadIntoBuffer` precedent) | token honored in-flight by SocketsHttpHandler streams | token pre-check only (`Stream.ReadAsync` referencesource); dispose/abort is the only in-flight interrupt; normal Close drains and can block → per-TFM adapter | `TryReset` (net6+, no polyfill, not needed); per-read linked CTS (allocation multiplier, worst on net472) |
| increment-4 encoding | `Utf8.IsValid` replaces `DecoderFallbackException` control flow (net8+ inbox; downlevel **requires adding `Microsoft.Bcl.Memory`** — maintainer decision); span `GetString`/`TryGetChars` on `#if NET` only; keep `(byte[], int, offset)` overloads downlevel; keep exact `ReadBufferAsString` parity (quote-strip substring is parity-conformant; optional well-known-charset span fast path) | net8/9/10 all native | Polyfill span-Encoding shims allocate copies without unsafe — do not use them on hot paths | `Rune` (nothing needs it; no downlevel existence); `SearchValues` (no multi-value scan in this policy) |
| sse-stage-2 | Byte-level framing per dotnet/runtime `SseParser`: pooled contiguous line buffer, `IndexOfAny((byte)'\r',(byte)'\n')`, CR-deferral at chunk boundary, BOM skip, payload stays UTF-8 into `Utf8JsonReader`; strictness delta (`Utf8.IsValid` per payload vs replacement parity) decided explicitly; adopting the `System.Net.ServerSentEvents` package (ns2.0-compatible, deps already satisfied) is a viable alternative to mirroring | vectorized byte scan | scalar scan via portable `System.Memory`; allocation wins persist, throughput wins need net472 benchmarks | `PipeReader` for this loop (runtime's own SSE parser doesn't use it; consumed/examined footguns; downlevel sequence handling); `SearchValues` for a two-byte set |

Open dependency decisions surfaced (not made) by this research: adding `Microsoft.Bcl.Memory`
(downlevel `Utf8.IsValid`) and/or `System.Net.ServerSentEvents` (framing outsource). Both are
Microsoft-owned, netstandard2.0-compatible, and dependency-light against the existing graph; both
still change the SDK's shipped dependency surface and belong to the maintainer per
`docs/architecture/platform-and-packaging.md`.
