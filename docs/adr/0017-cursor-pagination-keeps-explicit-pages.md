# Cursor pagination keeps explicit pages and adds item enumeration

Date: 2026-08-20

An operation whose pinned request and response bind to the supported cursor-list dialect keeps its
generated one-page `List*Async` method and additively emits `Enumerate*Async` as a lazy
`IAsyncEnumerable<TItem>`. The page method remains the cursor/metadata and `NoThrow` path; automatic
item traversal always throws API errors when their page is reached. This was chosen over a local
`AsyncPageable<T>`/`Page<T>` family because the generated response envelope already is the page,
including the bidirectional `ListCursor`, while an Azure-shaped wrapper would duplicate that public
vocabulary and invite an integer page-size contract absent from the pin.

The first request is sent unchanged. Continuations retain the pinned string `limit`, omit
first-page-only `order`, and pass the opaque returned `cursor.next`; only an absent next cursor ends
the sequence. Generated adapters project each admitted operation onto one hand-written traversal
core. Other pagination dialects require their own mechanically proven binding support rather than
being inferred from descriptions or forced through `ListRequest`.

## Consequences

- The automatic sequence is finite pull-based pagination over ordinary buffered HTTP responses,
  not a server stream and not a second transport path.
- Cancellation is observed during page requests and between already-buffered items.
- `cursor.previous` and page metadata remain available through explicit page calls; a future page
  sequence can be added without changing this surface if a concrete consumer earns it.
- Cursors are never decoded, normalized, incremented, compared, or cycle-checked (ADR-0013).
