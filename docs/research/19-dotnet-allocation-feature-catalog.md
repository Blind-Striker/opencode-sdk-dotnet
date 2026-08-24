# Modern .NET low-allocation feature catalog (.NET 7→10, C# 11→14)

Date: 2026-08-24

> Dated evidence and decision history, not current policy. Follow current canon through
> `AGENTS.md`.

This is the breadth leg to `17-modern-dotnet-allocation-apis.md` (the increment-gated deep
survey). Doc 17 answers "how exactly do we build increments 3/4 and SSE stage 2"; this catalog
answers "what allocation/copy-relevant machinery exists in .NET 7→10 and C# 11→14 at all, and
does any of it apply to this codebase" — including features neither document's authors set out
looking for. Where an item is already covered in doc 17, this file carries one line and a
reference, never a restatement.

Method and sources: first-party only — the learn.microsoft.com "What's new in .NET 7/8/9/10"
(libraries and runtime) and "What's new in C# 13/14" pages, API reference pages (moniker /
"Applies to" ranges read from page metadata), dotnet/runtime sources, and local artifact
verification against what this repository actually restores: Polyfill 11.0.2 `contentFiles`
source (file-by-file), `System.Memory` 4.6.3 `lib/net462` public-type enumeration (ilspycmd),
and a purpose-built IL probe (§0). Stephen Toub's "Performance Improvements in .NET 7/8/9/10"
posts are the deep-dive companions to the runtime claims; every claim below is anchored to a
page that was actually read.

Consumer context for the applicability filter: a typed HTTP SDK (HttpClient pipeline, pooled
body buffering, charset/BOM decode policy, SSE frame reader, System.Text.Json source-gen
materialization, query/route string building, Basic-auth header construction) plus a
Roslyn-emitting generator tool. Product TFMs `netstandard2.0;net472;net8.0;net9.0;net10.0`
("the five"); generator tool targets net10.0 only (`Directory.Build.props`:
`DefaultTargetFramework`). LangVersion 14 pinned everywhere. Downlevel packages: System.Memory
4.6.3 + Polyfill 11.0.2. `AllowUnsafeBlocks` off — sealed decision unless a measured case
reopens it. Every entry ends with "applies here" (naming the class/area) or "not applicable"
(one-line why), plus a gate tag: `increment-3` / `increment-4` / `sse-stage-2` /
`new-candidate` / `awareness-only`.

## 0. Verified codegen baseline: what the downlevel compiler actually emits

The maintainer's sharpest open question — what do the new span-shaped language features compile
to on TFMs *without* InlineArray runtime support — was answered by experiment, not docs: a
two-TFM probe (`net472;net8.0`, LangVersion 14, `AllowUnsafeBlocks` off, System.Memory 4.6.3,
SDK 10.0.303) built in Release and decompiled to IL with ilspycmd 10.1. Findings:

| Construct | net8.0 IL | net472 IL (portable span) |
|---|---|---|
| `params ReadOnlySpan<int>` call, constant args | `RuntimeHelpers.CreateSpan<int>` over RVA data — zero alloc | lazily initialized **cached static array** in `<PrivateImplementationDetails>` (first call `newarr` + `InitializeArray`, then reused) — amortized zero alloc |
| `params ReadOnlySpan<int>` call, variable args | compiler-synthesized `<>y__InlineArray2'1` on the **stack** + `InlineArrayAsReadOnlySpan` — zero heap alloc | `newarr` per call, wrapped in span — exactly the classic `params T[]` allocation, never worse |
| `params ReadOnlySpan<string>` call, constant args | stack inline array of `string` — zero heap alloc | cached static `string[]` — amortized zero alloc |
| `"utf-8"u8` literal | `ldsflda` RVA field + `ReadOnlySpan<byte>(void*, int)` ctor — zero alloc | **identical**: RVA pointer ctor, zero alloc, no `unsafe` needed in user code |
| `ReadOnlySpan<byte> b = [0xEF, 0xBB, 0xBF];` | RVA pointer ctor — zero alloc | **identical** — zero alloc (byte data is special-cased like u8) |
| `ReadOnlySpan<char> s = ['u','t','f'];` | `RuntimeHelpers.CreateSpan<char>` — zero alloc | lazily cached static `char[]` — amortized zero alloc |
| `Span<byte> b = stackalloc byte[16];` | `localloc` + span ctor | **identical** `localloc` — safe stackalloc needs no `AllowUnsafeBlocks` on any TFM |
| `s is "utf-8" or "us-ascii"` (span constant pattern) | span `SequenceEqual` | `MemoryExtensions.AsSpan(string)` + `SequenceEqual<char>` — allocation-free |
| `ref struct` with `ref int` field | compiles | **CS9064 "Target runtime doesn't support ref fields"** — hard error |

The probe also confirmed Roslyn synthesizes the required attributes as embedded types on
net472 (`ParamCollectionAttribute`, `ScopedRefAttribute`, `RefSafetyRulesAttribute`) — no
package needed to *compile* these features downlevel (Polyfill 11.0.2 ships
`ParamCollectionAttribute.cs`/`UnscopedRefAttribute.cs` anyway).

Net conclusion for shared code: span-shaped call sites are free-or-better on every TFM —
zero-alloc on net8+, amortized-zero (constants) or params-array-parity (variables) on
net472/ns2.0. `u8` literals and byte collection expressions are unconditionally zero-alloc on
all five targets. `ref` fields are modern-only, full stop.

## 1. Language (C# 11 → C# 14)

**UTF-8 string literals (`"…"u8`)** — C# 11.
Source: [C# 11 feature list](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history#c-version-11);
codegen verified in §0. TFMs: all five, zero-alloc everywhere (RVA-backed
`ReadOnlySpan<byte>`). Applies here: every fixed byte sequence in the pipeline — BOM prefixes
in `ResponseEncodingPolicy` (EF BB BF, FF FE, FE FF, FF FE 00 00), SSE field names
(`data`/`event`/`id`/`retry`) and delimiter bytes for the byte-level stage-2 reader,
well-known charset names for byte-wise compare. Gate: sse-stage-2, increment-4, new-candidate.

**`ref` fields in `ref struct`** — C# 11 language + .NET 7 runtime (`ByRefFields`).
Source: [What's new in .NET 7](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-7)
(ref fields runtime support); CS9064 verified in §0. TFMs: net8/9/10 only; net472/ns2.0 is a
compile error, so shared code cannot use them (a per-TFM `#if NET` type could). Applies here:
nothing in the pipeline needs a hand-rolled ref-field ref struct; `Utf8JsonReader` and spans
already carry the wins. Not applicable: no shared-code viability, no concrete need. Gate:
awareness-only.

**`scoped` parameters/locals** — C# 11. Compile-time-only lifetime annotation; Roslyn embeds
`ScopedRefAttribute` when absent (§0). TFMs: all five. Applies here: internal span-taking
helpers (buffer slicing, charset compare) — documents non-escape, enables callers to pass
stackalloc'd spans. Gate: new-candidate (style-level, zero cost).

**Method-group-to-delegate conversion caching** — C# 11.
Source: [C# 11 feature list](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history#c-version-11)
("the compiler may cache the delegate object created from a method group conversion" — no
per-call delegate allocation). TFMs: all five (cached static field, no runtime dependency).
Applies here: anywhere a static method group is passed as a callback (pipeline continuations,
`CancellationToken.Register(static …)` patterns) — a silent free win at LangVersion 14.
Gate: awareness-only.

**Span pattern matching against constant strings** — C# 11. Codegen verified in §0
(`AsSpan` + `SequenceEqual`, allocation-free on net472). TFMs: all five. Applies here:
`ResponseEncodingPolicy` well-known-charset fast path (`unquoted is "utf-8" or "us-ascii" …`)
without allocating the unquoted substring — the exact optimization doc 17 §1 flagged as
optional for the `charset[1..^1]` allocation. Gate: increment-4, new-candidate.

**Collection expressions (`[…]`) with span targets** — C# 12.
Source: [C# 12](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history#c-version-12);
codegen verified in §0. TFMs: all five; on net472, constant span targets become cached static
arrays (amortized zero), byte data becomes RVA spans (zero). Applies here: static lookup
tables and test fixtures; safe on hot paths only for constant data (variable-element spans
allocate per call downlevel, like params). Gate: new-candidate (low).

**Inline arrays (`[InlineArray]`)** — C# 12 language + .NET 8 runtime struct layout.
Source: [What's new in .NET 8 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime).
TFMs: net8/9/10 only; downlevel the attribute/layout doesn't exist (the *compiler-synthesized*
inline arrays in §0 are the consumption side, handled automatically). Applies here: no
fixed-size buffer struct need in the SDK; the compiler already uses them where it matters.
Not applicable: no direct declaration site. Gate: awareness-only.

**`params ReadOnlySpan<T>` / params collections** — C# 13.
Source: [What's new in C# 13](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13);
downlevel codegen verified in §0 (the headline experiment). TFMs: all five (compiles
everywhere; allocation profile differs as per §0). Applies here: internal variadic helpers
(e.g., accepted-media-type lists, multi-token header assembly) and future public convenience
overloads — modern callers get stack spans, net472 callers get params-array parity. Gate:
new-candidate.

**`allows ref struct` generic anti-constraint** — C# 13 + .NET 9 runtime
(`ByRefLikeGenerics`). Source: C# 13 page; [.NET 9 libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries)
("allows ref struct used in libraries" — e.g. `string.Create`'s `TState`). TFMs: net9/10
only. Applies here: not declarable in shared code; the benefit arrives indirectly through
net9+ BCL annotations (passing spans as `TState` to `string.Create` on modern legs).
Gate: awareness-only.

**`ref struct` interfaces** — C# 13 + .NET 9 runtime. Source: C# 13 page. TFMs: net9/10 only.
Not applicable: the SDK defines no ref struct abstractions; per-TFM-only utility. Gate:
awareness-only.

**`ref`/`unsafe` contexts in iterators and async methods** — C# 13 (compile-time borrow
checking only; no runtime dependency). Source: C# 13 page. TFMs: all five. Applies here:
async pipeline methods (`ResponseBodyReader`, SSE read loops) may now hold `Span<byte>`
locals between (not across) awaits — removes a long-standing reason to split span work into
sync local functions. Gate: increment-3/sse-stage-2 (ergonomics of the implementations).

**`OverloadResolutionPriorityAttribute`** — C# 13; attribute type is net9 inbox, Polyfill
11.0.2 ships it downlevel (`OverloadResolutionPriorityAttribute.cs`, verified). Source: C# 13
page. TFMs: usable on all five via Polyfill. Applies here: public-API evolution — steering
existing callers to later span-based overloads without breaking binary compat (the tool the
BCL itself used for its net9 params-span wave). Gate: new-candidate (API design reserve).

**First-class span conversions** — C# 14. Source:
[What's new in C# 14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
(implicit `T[]`→`ReadOnlySpan<T>`/`Span<T>` conversions, spans as extension receivers, better
generic inference). Compile-time feature over existing span types — works against
System.Memory downlevel. Applies here: silently improves overload resolution toward span
overloads on recompile everywhere; watch item for subtle overload-resolution behavior changes
(documented compiler breaking changes). Gate: awareness-only.

**Other C# 14 features** (extension members, `field`, null-conditional assignment, partial
constructors/events, compound assignment operators): no allocation/copy relevance for this
codebase. Not applicable: ergonomics only. Gate: none.

**Safe `stackalloc` into `Span<T>`** — C# 7.2+, restated because it's load-bearing: verified
in §0 to require no `AllowUnsafeBlocks` and to work identically on net472. Applies here:
small scratch buffers — charset unquote (≤ ~40 chars), header token normalization — with the
usual "constant small size, fall back to pool above threshold" discipline. Gate: increment-4,
new-candidate.

**`readonly struct` / `in` / `ref readonly` parameters** — C# 7.2/12. Copy avoidance for
larger structs; `ref readonly` (C# 12) adds definite-location semantics. TFMs: all five.
Applies here: the SDK's structs (`EncodedResponseBody`, pooled-buffer handles) are small;
`readonly struct` is already house style — `in` only pays above ~4 words. Gate: awareness-only.

**`[SkipLocalsInit]`** — C# 9 attribute, requires `AllowUnsafeBlocks` to compile. Polyfill
ships the attribute shape downlevel (`SkipLocalsInitAttribute.cs`), but the repo's sealed
unsafe-off decision blocks the feature entirely. Not applicable: blocked by recorded
decision; revisit only alongside a measured unsafe-reopening case. Gate: awareness-only.

## 2. Strings and formatting

**`string.Create(int, TState, SpanAction<char,TState>)`** — netcore2.1+; net9 adds
`allows ref struct` on `TState`. Downlevel: Polyfill 11.0.2 supplies the shape; without
unsafe its fallback rents a `char[]` from `ArrayPool`, runs the action, then copies into
`new string(chars, …)` (verified in `StringPolyfill.cs`) — one extra pooled buffer + copy vs
modern's in-place write, still better than StringBuilder chains. Applies here: final
materialization of query/route strings in generated operations — compute length, write once.
Gate: new-candidate.

**Interpolated string handlers / `DefaultInterpolatedStringHandler`** — C# 10 + .NET 6 BCL.
No Polyfill (verified: no handler type in 11.0.2 contentFiles), and that is *good*: on
ns2.0/net472 the same interpolation source lowers to `string.Format`/`Concat`, on net8+
it lowers to the handler (stackalloc + pooled growth, `ISpanFormattable` for args, no boxing).
Multi-targeting alone upgrades every interpolation on modern legs. Applies here: query/route
building, exception messages — write natural interpolations; the compiler does per-TFM the
right thing. Gate: new-candidate (verify no hand-rolled concat is pessimizing modern legs).

**`MemoryExtensions.TryWrite` (UTF-16 interpolated handler into `Span<char>`)** — net6+; no
downlevel (no Polyfill handler). `#if NET` only. Applies here: formatting into pooled/stack
char buffers for query building on modern legs. Gate: new-candidate (paired with
ValueStringBuilder below).

**`ISpanFormattable` (net6+) / `IUtf8SpanFormattable` (net8+) and primitive
`TryFormat`** — Source: [.NET 8 runtime page, "UTF8 improvements"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime)
(all primitives implement both). Downlevel: interfaces absent; Polyfill provides `TryFormat`
*extension shims* for primitives to char and UTF-8 byte spans, but the byte-span shims
allocate (`ToString()` then `Encoding.UTF8.TryGetBytes` — verified in
`Polyfill_TryFormatToByteSpan.cs`), so they are source-compat, not optimization. Applies
here: number/Guid formatting into buffers on `#if NET`; downlevel keeps `ToString`.
Gate: new-candidate (modern legs only).

**`Utf8.TryWrite` (UTF-8 interpolated handler into `Span<byte>`)** — net8+ (`System.Text.Unicode`).
Source: same .NET 8 page. Downlevel: absent — `Microsoft.Bcl.Memory`'s `Utf8` backport exports
only `IsValid`/`FromUtf16`/`ToUtf16` (doc 17 verification), Polyfill has nothing. Applies
here: composing UTF-8 request bodies or diagnostics directly as bytes on modern legs; today
the SDK serializes JSON via STJ (already UTF-8), so surface is small. Gate: new-candidate
(low), `#if NET8_0_OR_GREATER`.

**`Encoding.TryGetBytes`/`TryGetChars`** — net8+. One line: covered in doc 17 §1 (Polyfill
downlevel shims allocate; use array overloads downlevel). Gate: increment-4.

**`CompositeFormat`** — net8+ only ("Applies to" verified: net-8.0+, `System.Runtime`, no
package, no Polyfill). Parse-once format strings for `string.Format(…, CompositeFormat, …)`.
Not applicable: the SDK's only runtime format strings are exception messages — cold paths
that don't warrant a modern-only split. Gate: awareness-only.

**`System.Text.Ascii`** — net8+ only ("Applies to" verified: net-8.0..net-11.0,
`System.Runtime`, no downlevel package, no Polyfill). Byte/char-mixed `Equals`/
`EqualsIgnoreCase`, `IsValid`, `ToLower/ToUpper(InPlace)`, `Trim` — vectorized,
culture-unaware, and crucially able to compare `ReadOnlySpan<byte>` against
`ReadOnlySpan<char>` without decoding. Applies here: SSE stage-2 field-name recognition over
raw bytes (`Ascii.Equals(fieldBytes, "data")`), charset-token compare in
`ResponseEncodingPolicy` (`Ascii.EqualsIgnoreCase(charsetSpan, "utf-8")`), header token
checks — as `#if NET8_0_OR_GREATER` fast paths over the portable compare. Gate: sse-stage-2,
increment-4, new-candidate.

**String interning** — `string.Intern` is process-global, permanent, and lock-bearing; STJ
does not intern decoded property *values* (only property-name lookup avoids materializing
names). The modern replacement for "don't re-allocate the same small string" is a bounded
cache keyed by span (see `GetAlternateLookup`, §3) or fixed `static readonly` strings for
known values. Applies here: SSE event-type names (a handful of well-known values repeated per
frame) are the one hot repeated-string site — match bytes against known names, fall back to
allocation for unknown types. Gate: sse-stage-2 (design note), not `string.Intern`.

**ValueStringBuilder pattern** — internal to dotnet/runtime
([ValueStringBuilder.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.cs)),
intended to be copied. Verified against source: **no `unsafe` keyword, no pointers** — initial
`Span<char>` (typically stackalloc'd by the caller), `ArrayPool<char>.Shared` growth
(double-or-needed), `MemoryMarshal.GetReference` for pinning support, `Dispose` returns the
rented array. Compiles under this repo's unsafe-off rule on all five TFMs (portable span
downlevel). Note Polyfill's downlevel `StringBuilder.Append(ReadOnlySpan<char>)` is
`value.ToString()` without unsafe (verified) — so StringBuilder + span downlevel silently
allocates, which the copied pattern avoids. Applies here: query/route string building —
replaces StringBuilder chains with one final `ToString()` allocation. Gate: new-candidate.

**`MemoryExtensions` additions** — `Split`/`SplitAny` into `Span<Range>` (net8);
`Split(char|span)` returning `SpanSplitEnumerator` (net9); single-value `StartsWith`/
`EndsWith` (net9); `Count` (net8); `CommonPrefixLength`, `IndexOfAnyExcept`,
`ContainsAny*` (net7/8). Source: [.NET 9 libraries, "Spans"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries).
Downlevel: Polyfill 11.0.2 ships the net9 *enumerator-style* `Split`/`SplitAny` plus
`CommonPrefixLength`/`IndexOfAnyExcept`/`Contains*`/`StartsWith`/`EndsWith` as portable
implementations (verified file list: `Polyfill_Memory_SpanSplit*.cs` etc.) — same shapes on
all five TFMs, scalar speed downlevel. Applies here: `Content-Type` parameter splitting in
`ResponseEncodingPolicy` (`;`/`=` tokenization without `string.Split` arrays); SSE line field
splitting stays `IndexOf(':')` (single delimiter). Gate: increment-4, new-candidate.

**Index/Range (`s[1..^1]`)** — syntax works on all five (Polyfill ships `Index`/`Range` for
ns2.0); on *string* it still compiles to `Substring` (allocation) — the allocation-free form
is `AsSpan(1, len-2)`. One-line trap note; doc 17 §1 covers the charset case. Gate:
increment-4.

## 3. Collections and lookup structures

**`FrozenDictionary<K,V>` / `FrozenSet<T>`** — net8+ inbox; downlevel available: "Applies to"
verified to include netstandard-2.0/net462 **via the `System.Collections.Immutable` package**
(v8+). Read-optimized immutable lookups; construction cost is deliberately front-loaded;
specialized implementations for string keys with `OrdinalIgnoreCase`. Source:
[.NET 8 runtime page, "Performance-focused types"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime).
Applies here: generated union discriminator → variant dispatch tables in the source-gen
materialization layer; the well-known-charset table in `ResponseEncodingPolicy`; generator
tool keyword/name tables (net10.0 — inbox, no decision needed). For the SDK's five TFMs,
downlevel use requires **adding System.Collections.Immutable** — a maintainer package
decision like `Microsoft.Bcl.Memory` in doc 17; the no-package alternative is `#if NET8+`
frozen / downlevel `Dictionary` behind one factory. Gate: new-candidate.

**`Dictionary/HashSet.GetAlternateLookup<ReadOnlySpan<char>>`** — net9+ (with C# 13
`allows ref struct`). Source: [.NET 9 libraries, "Collection lookups with spans"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries).
Downlevel: Polyfill ships `DictionaryAlternateLookup` — but its own doc comment states it
performs **O(n) linear scans** (verified), so it is correctness-only, never a hot-path
substitute. Applies here: the SSE event-type-name cache (§2 interning entry) — span-keyed
lookup avoids allocating a string per frame just to probe the cache; net9/10 legs only, with
downlevel falling back to allocate-and-look-up. Gate: sse-stage-2, new-candidate.

**`CollectionsMarshal.AsSpan(List<T>)` (net5+) / `GetValueRefOrAddDefault` (net6+)** —
modern-only in practice: Polyfill's `AsSpan` goes through cached reflection
(`FieldInfo.GetValue` per call, verified) and it has **no** `GetValueRefOrAddDefault`.
Applies here: generator tool (net10.0) freely — e.g. count-then-emit loops over `List<T>`
without enumerator or copies; SDK shared code only under `#if NET`. Gate: new-candidate
(generator), awareness-only (SDK).

**`SearchValues<T>` (net8) / `SearchValues<string>` (net9)** — covered in doc 17 §1: no
downlevel existence, and no multi-value scan in this pipeline needs it (two-byte CR/LF scans
use `IndexOfAny`). The net9 substring-set expansion changes nothing for this codebase. Gate:
none (see doc 17).

**`OrderedDictionary<K,V>` (net9), `ReadOnlySet<T>` (net9), `PriorityQueue.Remove`** — not
applicable: no ordered-map/read-only-set surface in the SDK's hot or public paths. Gate: none.

## 4. Buffers, encoding, and IO

**`ArrayPool<byte>` discipline; pooled contiguous buffering blueprint** — covered in depth in
doc 17 §2 (net10 `LimitArrayPoolWriteStream` as the reference design; `CopyToAsync` intake;
`clearArray` decision). Gate: increment-3 (see doc 17).

**`MemoryPool<T>` / `IMemoryOwner<T>`** — available on all five via System.Memory, but
`MemoryPool.Shared.Rent` allocates an owner object per rent (it wraps `ArrayPool`). Not
applicable: `ArrayPool` + a single-owner struct/class (the `PooledResponseBodyBuffer`
experiment shape) strictly dominates for this pipeline. Gate: awareness-only
(anti-recommendation).

**`System.Buffers.Text`: `Utf8Parser`, `Utf8Formatter`, `Base64`** — verified present in
`System.Memory` 4.6.3 `lib/net462` (type listing), so all five TFMs at scalar speed downlevel.
Culture-invariant parse/format directly over UTF-8 bytes. Applies here: SSE `retry:` digit
parsing straight from the byte payload in the stage-2 reader (no decode, no `int.Parse`);
`Utf8Formatter` if UTF-8 body composition ever appears. Gate: sse-stage-2.

**`Base64Url`** — net9+ inbox; downlevel doubly covered (Polyfill 11.0.2 `Base64Url.cs` and
`Microsoft.Bcl.Memory`, both verified). Not applicable: the opencode API surface uses no
URL-safe base64. Gate: awareness-only.

**Basic-auth header construction** — `Convert.ToBase64String` allocates, and
`Convert.TryToBase64Chars` (netcore2.1+) plus stackalloc could avoid intermediates — but the
header value is built **once per client instance** and cached on `HttpClient`/request
defaults. Not applicable: cold path; allocation profile irrelevant by construction. Gate:
none (honest NA for a named codebase area).

**`GC.AllocateUninitializedArray<T>`** — net5+ (no downlevel, no polyfill). Skips zeroing for
arrays that will be fully overwritten; the runtime's own `LimitArrayPoolWriteStream.CreateCopy`
uses exactly this for escape copies. Applies here: increment-3's escaping-array paths
(`ToArray`-equivalents from the pooled buffer) under `#if NET`; downlevel `new byte[]`.
Gate: increment-3, new-candidate (small).

**`Encoding.CreateTranscodingStream`** — net5+ (no downlevel). Wraps a stream to convert
between encodings on the fly — the theoretical answer to "SSE body in a non-UTF-8 charset
feeding a byte-level frame scanner". Not applicable: the WHATWG event-stream spec mandates
UTF-8 decoding for `text/event-stream`
([HTML Standard §9.2.5](https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation):
streams "must be decoded as UTF-8"), so the byte-level reader may treat non-UTF-8 as protocol
error rather than transcode. Gate: sse-stage-2 (decision note), otherwise NA.

**`Stream.ReadExactly` / `ReadAtLeast`** — net7+; Polyfill supplies downlevel shapes. Not
applicable: HTTP body reads are read-what-arrives loops; exact-count reads fit length-prefixed
protocols, not this pipeline. Gate: awareness-only.

**`BitOperations`** (`RoundUpToPowerOf2` etc., net6 for the useful parts) — not applicable:
buffer bucket sizing is `ArrayPool`'s job; no bit-twiddling site exists in an HTTP SDK's hot
path. Gate: awareness-only (the "only if genuinely applicable" answer is: it isn't).

**`Microsoft.IO.RecyclableMemoryStream`** — one line: doc 17 §2 evaluated and deferred it
(segmentation costs, no need at current body sizes). Gate: increment-3 fallback (see doc 17).

**`Microsoft.Extensions.ObjectPool`** — package (verified: 10.0.x targets ns2.0/net462/net10,
zero dependencies). General object pooling with policies (`StringBuilderPooledObjectPolicy`
included). Applies here: weakly — the pipeline's poolable state is byte buffers (ArrayPool)
and char building (ValueStringBuilder pattern); no long-lived reusable object graph exists to
justify a new dependency. Gate: awareness-only (named alternative if one ever appears).

**`HttpHeaders.NonValidated`** — net6+ ("Applies to" verified: net-6.0+, absent downlevel).
Returns `HttpHeadersNonValidated`, a view that neither parses nor validates on access —
avoids the lazy header parsing/validation allocations of the typed accessors and, on
SocketsHttpHandler responses, can read raw stored values. Applies here: pipeline header
reads that only need raw strings (`Content-Type` for the encoding policy reads a *parsed*
`ContentType` today — switching means taking over charset extraction; do it only where the
policy already tokenizes by hand). `#if NET` fast path; net472 keeps typed access. Gate:
increment-4, new-candidate (low).

## 5. Async and infrastructure

**`ValueTask` discipline** — one line: doc 17 §5 (public surfaces stay `Task`; internal
`#if NET` seams may be `ValueTask`-shaped). Gate: increment-3 (see doc 17).

**`PoolingAsyncValueTaskMethodBuilder`** — net6+ ("Applies to" verified; no downlevel).
Opt-in per method via `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]` on
`ValueTask`-returning async methods; pools the state-machine boxes that async completion
otherwise allocates per call. Applies here: only the hottest internal `ValueTask` methods
(per-read loop bodies in increment-3 / SSE) under `#if NET`, and only with BenchmarkDotNet
evidence — Toub's guidance is that pooling wins are workload-dependent and can regress.
Gate: new-candidate (measured, low priority).

**`IAsyncEnumerable<T>` allocation patterns** — the compiler allocates one state
machine/enumerator per enumeration (not per item); `[EnumeratorCancellation]` +
`WithCancellation` avoids wrapper allocations; `await foreach` over a struct-returning custom
enumerable is possible but hostile to public API. Applies here: the SSE public surface
already returns `IAsyncEnumerable` — per-frame cost is dominated by payload handling, not the
iterator; keep one enumeration per response, avoid LINQ-style async operators in the
pipeline. Gate: sse-stage-2 (guidance).

**`ConfigureAwait(ConfigureAwaitOptions)`** — net8+. Downlevel: Polyfill shims the shape but
implements `SuppressThrowing`/`ForceYielding` by wrapping **additional async state machines**
(verified in `Polyfill_Task.cs`) — allocation-adding, not saving. Applies here:
`ConfigureAwait(false)` remains the pipeline rule; `SuppressThrowing` could simplify
best-effort cleanup awaits on `#if NET`, never through the downlevel shim on hot paths.
Gate: awareness-only.

**`TimeProvider`** — net8+ inbox
([.NET 8 runtime page, "Time abstraction"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime));
downlevel via `Microsoft.Bcl.TimeProvider` package (Microsoft-owned, ns2.0). Allocation-
neutral; the win is deterministic testing of timer-driven logic. Applies here: increment-3's
progress timeout (`CancelAfter` re-arm) is exactly the hard-to-test timer machinery
`TimeProvider` exists for — `CancellationTokenSource` has no TimeProvider hook, but the
deadline bookkeeping around it can take one. Downlevel needs the package (maintainer
decision) or an internal clock seam. Gate: increment-3, new-candidate (testability).

**`CancellationToken.UnsafeRegister`** — netcore3+; Polyfill maps it to plain `Register`
downlevel (verified) — same semantics minus the `ExecutionContext`-capture savings. Applies
here: the operation-scoped dispose-on-cancel registration in increment-3 (doc 17 §4's
`LoadIntoBuffer` precedent) — static callback + state, no context flow needed. Free
modern-leg win, source-identical downlevel. Gate: increment-3.

**`System.Threading.Lock`** — net9+ (Polyfill ships a downlevel `Lock`). Contention
ergonomics, not allocation. Not applicable: the pipeline is per-operation single-owner by
design; no lock sites on hot paths. Gate: awareness-only.

**`Task.WhenEach`** (net9), **prioritized channels** (net9) — not applicable: no fan-out task
aggregation or producer/consumer queues in the SDK. Gate: none.

## 6. System.Text.Json, net8→10, through the "typed HTTP SDK" filter

Baseline: the SDK ships `System.Text.Json` 10.0.11 on **all five TFMs** (doc 17 table), so
STJ API availability is mostly *package-version*, not TFM, gated — several "net9/net10"
features below are usable even on net472/ns2.0. Verified per entry via "Applies to" monikers.

**UTF-8 span `Deserialize` overloads / `Utf8JsonReader` over spans and sequences** — one
line: already in use; doc 17 §3. Gate: sse-stage-2.

**`JsonMarshal.GetRawUtf8Value(JsonElement)`** — API introduced with .NET 9; "Applies to"
verified to include ns2.0/net472 **via the System.Text.Json package** → available on all five
TFMs today. Returns a `ReadOnlySpan<byte>` view of an element's raw UTF-8 without
re-serialization (`JsonElement.GetRawText()` allocates a string). Caveat from the API
remarks: the span aliases the parent `JsonDocument`'s pooled buffer — invalid after dispose.
Applies here: union/variant materialization diagnostics and raw-payload capture
(`EncodedResponseBody`-adjacent error reporting): today any "unrecognized variant" detail
string pays UTF-16 re-encoding; this doesn't. Gate: new-candidate.

**`JsonSerializerOptions.AllowOutOfOrderMetadataProperties`** — .NET 9 wave; all five TFMs
via the package (moniker-verified). Relaxes `$type`-first ordering for polymorphic
deserialization at a documented cost: whole-object buffering and O(n×d) backtracking. Not
applicable today: the SDK deliberately materializes unions structurally, not via `$type`
metadata — record as the known switch if metadata polymorphism is ever adopted, with its
allocation penalty attached. Gate: awareness-only.

**`Utf8JsonReader` `AllowMultipleValues` (JsonReaderOptions)** — net9 wave
([.NET 9 libraries, "Stream multiple JSON documents"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries));
package-carried. Reads whitespace-separated top-level JSON values from one buffer/stream.
Not applicable: SSE framing owns stream segmentation (each `data` payload is a complete
document); would matter only for an NDJSON endpoint, which opencode does not expose. Gate:
awareness-only.

**`JsonSerializer` `PipeWriter` (net9 wave) / `PipeReader` (net10 wave) overloads** —
package-carried ([.NET 10 libraries, "PipeReader support"](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries)).
Serialize/deserialize directly against pipes without a Stream adapter. Not applicable while
the pipeline stays Stream-plus-pooled-buffer shaped — doc 17 §3 records the evidence that
`PipeReader` is the wrong fit for the SSE loop; this entry just notes STJ would cooperate if
that decision were ever reversed. Gate: awareness-only.

**`AllowDuplicateProperties = false` / `JsonSerializerOptions.Strict`** — net10 wave,
package-carried. Correctness/security hardening (duplicate-key smuggling), not allocation.
Applies here: worth a line in the materialization options review, outside this catalog's
allocation scope. Gate: awareness-only.

**`JsonSerializerOptions.Web`, `JsonSchemaExporter`, enum-name attributes, indentation
options** — not applicable: the SDK pins its own source-gen options; no schema emission at
runtime. Gate: none.

## 7. Runtime "free wins" (awareness only — no code change implied)

These arrive by running on newer runtimes; the net472 leg gets none of them, which is exactly
why `docs/engineering/quality-gates.md` demands net472-leg benchmark evidence for downlevel
claims. Sources: the "What's new in .NET 8/9/10 runtime" pages read for this catalog; deep
dives in Toub's per-release performance posts.

| Win | Since | What it does for this SDK |
|---|---|---|
| Dynamic PGO on by default | net8 | Tier-1 recompiles use runtime profiles: hot pipeline paths get devirtualization and better inlining for free |
| PGO type-check/cast profiling | net9 | Cheaper `is`/cast fast paths in materialization dispatch |
| On-stack replacement + tiering | net7 | Long loops (SSE read loops) escalate to optimized code mid-run; also why BenchmarkDotNet warmup matters and why net472 (no tiering) benches differently |
| DATAS GC | net8 opt-in, **net9 default** | Heap sized to live data — an idle SDK-hosting process no longer holds peak-throughput heaps; changes memory-usage baselines between net8 and net9 runs |
| Object stack allocation: unescaped boxes | net9 | Boxing in cold generic paths can vanish under inlining |
| Stack allocation: small fixed value-type arrays, small ref-type arrays, struct-field and delegate escape analysis | net10 | Small temporary arrays and non-escaping lambdas/`Func` objects in the pipeline stop hitting the heap |
| Array interface devirtualization + enumeration de-abstraction | net10 | `IEnumerable<T>`-over-array loops approach indexed-loop cost — softens LINQ-shaped cold paths |
| Faster exception handling (2–4×) | net9 | Cheaper error channel; does not license exceptions as control flow (the `Utf8.IsValid` motivation in doc 17 stands) |
| Loop optimizations (IV widening, strength reduction, downcounting), improved inlining incl. shared generics | net9 | Span scan loops (`IndexOfAny`, copy loops) get tighter codegen |
| Arm64 write-barrier improvements | net10 | 8–20% GC pause improvements on Arm64 hosts |
| `params ReadOnlySpan<T>` overloads across the BCL (60+ methods) + C# 14 first-class spans + `OverloadResolutionPriority` steering | net9/C# 14 | Recompiling against net9+/LangVersion 14 silently rebinds existing calls (e.g. `string.Join`) to span overloads — allocation removed with zero diff |

## 8. Ranked outcome tables

### New-candidate items, ranked by expected value for this codebase

| # | Item | Where | TFM story | Cost to adopt |
|---|---|---|---|---|
| 1 | `u8` literals for all fixed byte data | `ResponseEncodingPolicy` BOM tables; SSE field names/delimiters (stage 2) | zero-alloc on all five (verified §0) | trivial; no dependency |
| 2 | Span constant-string patterns + `AsSpan` compare for well-known charsets | `ResponseEncodingPolicy` unquote/compare path | allocation-free on all five (verified §0) | small; removes the quoted-charset substring on the fast path |
| 3 | `System.Text.Ascii` byte/char token compares | SSE stage-2 field names; charset tokens | net8+ only; `#if NET` fast path over portable compare | small; dual-path tests |
| 4 | `JsonMarshal.GetRawUtf8Value` | union materialization raw-payload capture/diagnostics | all five via STJ 10 package (moniker-verified) | small; lifetime caveat needs a comment + test |
| 5 | `FrozenDictionary`/`FrozenSet` | generated discriminator dispatch; charset table; generator tables | net8+ inbox; downlevel needs System.Collections.Immutable (maintainer decision); generator (net10) free today | medium; package decision for the SDK legs |
| 6 | ValueStringBuilder pattern (copied) + interpolated handlers | query/route building in generated operations | pattern compiles unsafe-free on all five; handlers upgrade net8+ automatically | medium; one internal type + tests |
| 7 | `params ReadOnlySpan<T>` on internal/public variadic helpers | header/media-type helpers; future API surface | verified: free modern, params-parity downlevel | small; API review per doc 17 house rules |
| 8 | `TimeProvider` for the progress timeout | increment-3 deadline bookkeeping, tests | net8+ inbox; downlevel = Microsoft.Bcl.TimeProvider or internal clock seam | medium; testability payoff |
| 9 | `CancellationToken.UnsafeRegister` | increment-3 operation-scoped dispose registration | modern win; Polyfill maps to `Register` downlevel — source-identical | trivial |
| 10 | `GC.AllocateUninitializedArray` | increment-3 escaping-copy paths | `#if NET`; downlevel `new byte[]` | trivial |
| 11 | `GetAlternateLookup` span-keyed event-type cache | SSE stage-2 event-name reuse | net9/10 only; Polyfill shim is O(n) — fallback allocates instead | small; only if frame profiling shows event-name churn |
| 12 | `HttpHeaders.NonValidated` header reads | pipeline `Content-Type` access | net6+ (so net8/9/10 legs); absent downlevel | small; only where the policy already hand-tokenizes |
| 13 | `OverloadResolutionPriority` | future span-overload API evolution | all five via Polyfill attribute | reserve tool, no action now |
| 14 | `PoolingAsyncValueTaskMethodBuilder` | hottest internal async seams | net6+ only; measurement-gated | do not adopt without BDN evidence |

### Awareness-only ledger (no action; recorded so "we didn't know" can't recur)

Runtime free wins (§7 table); `ref` fields (CS9064 downlevel); inline-array declarations;
`allows ref struct` / ref-struct interfaces (net9+); `SkipLocalsInit` (blocked by the
unsafe-off decision); C# 14 first-class span conversions (watch overload-resolution breaking
changes on recompile); method-group delegate caching (already active); `CompositeFormat`
(cold paths only here); `Base64Url` (no API surface); Basic-auth span construction (once per
client, cached); `MemoryPool<T>` (dominated by ArrayPool); `BitOperations` (no applicable
site); `Stream.ReadExactly`/`ReadAtLeast` (wrong read model); `CreateTranscodingStream` (SSE
is UTF-8 by WHATWG mandate); `ObjectPool` package (no reusable object graph);
`ConfigureAwaitOptions` (downlevel shim allocates); `System.Threading.Lock`, `Task.WhenEach`,
prioritized channels (no sites); STJ `AllowOutOfOrderMetadataProperties` (structural unions
make it moot; O(n×d) cost recorded), `AllowMultipleValues` (SSE owns framing), pipe overloads
(Stream-shaped pipeline per doc 17), `Strict`/duplicate-property options (correctness review,
not allocation); SearchValues including net9 `SearchValues<string>` (doc 17: no multi-value
scan here); `OrderedDictionary`/`ReadOnlySet`/`PriorityQueue.Remove` (no sites); .NET 10
UTF-8 hex-conversion and span string-normalization APIs (no hex/normalization sites); Regex
span APIs incl. `EnumerateSplits` (no regex in SDK or generator); `WebSocketStream` (no
websocket transport); Tensor/SIMD surfaces (out of domain).

Open dependency decisions surfaced (not made) by this catalog, additive to doc 17's list:
`System.Collections.Immutable` (downlevel Frozen collections) and `Microsoft.Bcl.TimeProvider`
(downlevel time abstraction). Both Microsoft-owned and ns2.0-compatible; both change the
shipped dependency surface and belong to the maintainer per
`docs/architecture/platform-and-packaging.md`.
