# 📝 Requests

Date: 2026-09-17

Every operation that sends anything takes one request record. They are plain immutable records you
fill with an object initializer — with one twist worth knowing before you write your first `PATCH`:
some members can say *"leave this alone"* and *"clear this"* as two different things.

- [🧱 The request record](#-the-request-record)
- [🔀 Absent, null, and set](#-absent-null-and-set)
- [🔎 Reading a member back](#-reading-a-member-back)
- [🔗 Query members](#-query-members)
- [📍 Per-call location](#-per-call-location)

## 🧱 The request record

A request is a `sealed record` with `init`-only members. Required members are C# `required`, so the
compiler refuses an initializer that omits one; everything else you leave out:

```csharp
var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
{
    Title = "Fix the build",
    Model = new ModelRef { Id = "claude-sonnet-4-5", ProviderId = "anthropic" },
});
```

When every member is optional, the request itself is optional — `await session.UpdateSessionAsync()` sends `{}`. Records are values, so `with` gives you a variation
without touching the original.

## 🔀 Absent, null, and set

A member the API document declares **both optional and nullable** is typed `Optional<T?>` instead
of `T?`, because for those members the server can tell three things apart:

| You write | Wire | Means |
|---|---|---|
| nothing | the member is not written at all | leave whatever the server has |
| `Title = null` or `Title = Optional<string?>.Null` | `"title": null` | send an explicit null |
| `Title = "Revised title"` | `"title": "Revised title"` | set this value |

Setting a value needs no ceremony — an implicit conversion takes the plain value, so `Title =
"Revised title"` is what you write. `default` is the absent state, which is why an unassigned member is
absent for free.

The session update request illustrates the three wire states:

```csharp
var session = client.Sessions.GetSessionClient("ses_1");
await session.UpdateSessionAsync(new SessionUpdatePatchRequest { Title = "Revised title" });
await session.UpdateSessionAsync(new SessionUpdatePatchRequest()); // {}
await session.UpdateSessionAsync(new SessionUpdatePatchRequest { Title = Optional<string?>.Null }); // {"title":null}
```

The operation returns 204; use `GetSessionAsync` to observe the resulting session. What an explicit
null does is the server's decision. The SDK preserves the wire distinction and does not promise
that null always clears a value.

The experimental config update differs: `ExperimentalConfigUpdatePatchRequest.Shell` is a
**required nullable string**, so callers must supply a shell value or null. There is no omitted
shell state. A null removes the persisted shell preference; its 204 response has no read-back body.

The rule is keyed on the schema, not on the direction of one call: a schema a request body reaches
carries the wrapper everywhere it is used. A response-only property is untouched and stays an
ordinary `string?`, `ModelRef?`, and so on. One schema in the API is shared both ways —
`ToolFileContent`, which a session import sends and four read operations return — so its `Name`
carries the wrapper on the read side too and is read through `.Value`:

```csharp
var name = content is ToolFileContent file ? file.Name.Value : null;
```

## 🔎 Reading a member back

`Optional<T?>` has two members. `IsSet` is false only for the absent state, and `Value` is the
carried value — `null` both when the member is absent and when it carries an explicit null:

```csharp
var request = new SessionUpdatePatchRequest { Title = Optional<string?>.Null };

Console.WriteLine(request.Title switch
{
    { IsSet: false } => "absent",
    { Value: null } => "explicit null",
    { Value: var title } => title,
});
```

Equality follows the same three states: `default` does not equal `Optional<string?>.Null`, and two
members carrying the same value are equal. To go back to absent, assign `default`.

## 🔗 Query members

Some operations put their inputs in the query string rather than the body, and the request record
carries those members instead. They are ordinary nullable members — a query member that is null is
simply not written, and the server's own default applies:

```csharp
var export = await client.Experimental.GetSessionExportAsync("ses_1", new ExperimentalSessionExportRequest
{
    Sanitize = QueryBoolean.True,
});
```

`QueryBoolean` exists because the API declares these as the *strings* `"true"` and `"false"`, not
as JSON booleans. `QueryBoolean.True` writes `true`, `QueryBoolean.False` writes `false`, and null
writes nothing.

A request record can carry both at once — the body members serialize, the query members go into the
URL, and the initializer does not distinguish them. `PluginCheckPostRequest.Target` is a body
member and its `Location` is a query member:

```csharp
var check = await client.Plugins.CheckPluginUpdatesAsync(new PluginCheckPostRequest
{
    Target = "eslint",
    Location = new LocationSelector { Directory = "/repo/service" },
});
```

## 📍 Per-call location

The directory a call addresses reaches the server through two different channels, and
which one an operation reads is the document's decision, not yours to pick.

`OpenCodeRequestOptions.Location` is the per-call override of the ambient
`x-opencode-directory` header. It merges over
`OpenCodeClientOptions.Location` by directory — a set directory wins, an unset one inherits — and
only operations that resolve location from those headers read it:

```csharp
var scoped = await client.Sessions.ListSessionsAsync(requestOptions: new OpenCodeRequestOptions
{
    Location = new LocationSelector { Directory = "/repo/service" },
});
```

Most request records also carry a `Location` member of their own, for the operations whose document
declares a location **query** parameter. That one is part of the request, rides the URL, and is
independent of the headers above:

```csharp
var plugins = await client.Plugins.ListPluginsAsync(new PluginListRequest
{
    Location = new LocationSelector { Directory = "/repo/service" },
});
```

The options object also carries [`NoThrow`](errors-and-responses.md#-ask-for-the-failure-as-data-instead),
so one per-call argument selects both it and the header override.

---

Next: [errors and responses](errors-and-responses.md) for what comes back, or
[pagination](pagination.md) for the two operations that answer in pages.
