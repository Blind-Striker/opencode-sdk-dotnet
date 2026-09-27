# A trailing-wildcard route binds upstream's own codegen rule

Date: 2026-09-27

Upstream declares `fs.read` on the framework route `/api/fs/read/*`: the file path is the URL
tail, and the OpenAPI document declares no parameter for it, so a generator that reads only the
document cannot call the operation (ADR-0013). Upstream's first-party client can, because its own
pinned codegen has one deterministic rule for the case (`promiseWildcardInput`): a path that ends
in a single trailing `*` gets a required string input named `path` — `wildcard` when an input
already takes `path` — and `encodePath` fills the tail by escaping each `/`-separated segment with
`encodeURIComponent`. The generator adopts exactly that rule, and nothing else, as a second
protocol artifact under ADR-0013's reversal trigger: the route-tail member is required, the first
member of the operation's request record, never sent as a query parameter, and escaped segment by
segment as upstream's client escapes it. The rule is mirrored, not read: the generator never loads
upstream code; the source watch pins the codegen file with its behaviour, and
`tools/oracles/wildcard-path.ts` calls upstream's generated client over a corpus so a test holds
the SDK's request paths to the paths upstream's client sends. Ingestion keeps refusing any wildcard
that is not one trailing `/*`, as upstream's codegen refuses it. A dot segment in the tail is
refused by name, where upstream's URL parser would resolve it silently and address a different file.

The same operation answers `application/octet-stream` with a `string`/`format: binary` schema, which
the document states directly, so its binding needs no second artifact: a raw-byte success binds a
binary envelope that carries the buffered bytes as `Content` and the server's `Content-Type` as
`ContentType` — the server serves the file's MIME type, which upstream's client drops — while the
error statuses keep their JSON bodies and typed errors.

## Considered options

- A hand-written door for the one route: it copies the same encoding by hand, needs the generator
  to emit a partial client to host it, and would need another door for the next wildcard route.
- Staying declined until upstream declares the parameter: the surface would keep a file read that
  upstream's own client performs.

## Reversal trigger

Reconsider if upstream's OpenAPI document starts declaring the tail as a parameter (the rule then
duplicates the document, and a refresh shows it), or if upstream's codegen changes the rule (the
source watch trips at the refresh that brings it).
