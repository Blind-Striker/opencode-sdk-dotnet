# Union membership is an interface, not a base class

Date: 2026-08-19

A generated **marked union** is emitted as an interface declaring the union's discriminator, and each
wire schema stays one `sealed record` implementing every union it belongs to. Membership is not
expressible as a base class because a schema can belong to more than one union: 39 of the 40
branches of `Session.Event.Durable` are also direct branches of the 87-branch `V2Event`, so the
same leaf must answer to both the durable log stream and the live event bus. C# allows one base
class, and the binder already refuses this by name — *"schema cannot derive from both '…' and
'…'"* — which would leave `v2.event.subscribe`, the surface every upstream front-end consumes
(research doc 02), permanently ungenerable. Interfaces make membership plural, and because both
unions discriminate on the same wire field the leaf satisfies both contracts with one property.

## Scope: marked versus structural unions

A marked union has a common literal discriminator whose value selects an object schema:

```json
{ "type": "session.created", "data": {} }
{ "type": "session.deleted", "data": {} }
```

`SessionCreated` and `SessionDeleted` are wire schemas in their own right, so implementing `IEvent`
expresses membership without wrapping either object. A structural union has no such marker; its
branch is selected from the JSON value shape instead:

```json
"hello"
42
true
["a", "b"]
```

Primitive and collection values cannot implement a generated interface. Token-distinct structural
unions therefore use the generated carrier decision in ADR-0016, not this membership mechanism.

The discriminator is the interface's whole member set and does not grow, so this is not the
member-growth hazard that keeps client types free of interfaces. Grouping composes for free:
`ISessionEventDurable : ISessionLogItem` costs nothing, which lets a consumer ask
`item is ISessionEventDurable` instead of comparing the marker string the way upstream's own TUI
must. It also decouples the type graph from the dispatch path — an interface has no instances to
construct, so an outer union's converter reads a nested family's leaves directly rather than
re-parsing the payload into a second converter.

**Fail-closed wall this admits.** Two unions a leaf belongs to may declare markers with different
names — the leaf then carries both properties, which the wire object must anyway to stay
discriminable in both contexts. Same name with a different kind is refused instead: one JSON
field cannot be both a string and a number, so that is a contradiction in the spec rather than a
shape to model.

Mechanism verified before sealing, by compile and round-trip probe: `JsonConverterAttribute`
targets interfaces; a source-generated context deserializes into an interface-typed member and
into the interface directly; interface inheritance answers `is`. The `net472` leg compiles here
and is exercised by the Windows CI matrix (ADR-0002).

## Considered options

- **Keep abstract-record bases and refuse the collision** — fail-closed and already implemented,
  but it makes the live event bus ungenerable, against M5's complete generation profile.
- **Flip a union to an interface only when a leaf gains a second parent** — mechanical, but a
  spec refresh would then turn a shipped `abstract record` into an interface; the extend-only
  evolution posture exists to prevent exactly that.
- **Duplicate a leaf type per union** — rejected: it breaks one type per wire schema (ADR-0004)
  and makes a durable event untypable where a live event is expected.
- **Give the nested family a base that derives from the outer union** — rejected: it would claim
  live-bus membership for `session.usage.recorded`, which the spec denies. Projection never
  invents (ADR-0003).
