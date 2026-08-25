using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>Binds the <see cref="OperationPlan.Envelope"/> facet of a non-streaming success.</summary>
internal sealed class EnvelopeFacetBinder(OperationFacetContext context)
{
    private readonly OperationFacetContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public EnvelopePlan? Bind(SpecResponse success)
    {
        ArgumentNullException.ThrowIfNull(success);

        if (success.StatusCode is 204)
        {
            return BindNoContentEnvelope(success);
        }

        if (success.ContentType is not { IsJson: true } || success.Schema is null)
        {
            _context.Refuse("the success response must carry a JSON schema");
            return null;
        }

        var payload = success.EnvelopeShape switch
        {
            SpecEnvelopeShape.Bare => BindBarePayload(success.Schema),
            SpecEnvelopeShape.Data => BindDataPayload(success.Schema),
            SpecEnvelopeShape.CursorData => BindCursorListPayload(success.Schema),
            SpecEnvelopeShape.DataLocation => BindDataLocationPayload(success.Schema),
            SpecEnvelopeShape.None or SpecEnvelopeShape.DataHasMore or _ =>
                _context.RefuseNull($"envelope shape '{success.EnvelopeShape}' is not supported"),
        };
        var locationTypeName = success.EnvelopeShape is SpecEnvelopeShape.DataLocation
            ? BindLocationSibling(success.Schema)
            : null;
        if (success.EnvelopeShape is SpecEnvelopeShape.DataLocation && locationTypeName is null)
        {
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        var responseTypeName = OperationNamePolicy.ResponseTypeName(_context.Operation);
        var payloadName = DerivePayloadName(responseTypeName);
        if (payloadName is null)
        {
            return null;
        }

        var kind = success.EnvelopeShape switch
        {
            SpecEnvelopeShape.Data => EnvelopeKind.Data,
            SpecEnvelopeShape.CursorData => EnvelopeKind.CursorList,
            SpecEnvelopeShape.DataLocation => DataLocationKind(success.Schema),
            SpecEnvelopeShape.Bare or SpecEnvelopeShape.None
                or SpecEnvelopeShape.DataHasMore or _ => EnvelopeKind.Bare,
        };
        return new EnvelopePlan
        {
            ResponseTypeName = responseTypeName,
            AdapterTypeName = $"{responseTypeName}Adapter",
            PayloadName = payloadName,
            PayloadTypeName = payload,
            Kind = kind,
            SuccessStatusCode = 200,
            EnvelopeDtoTypeName = kind is EnvelopeKind.Bare ? null : $"{responseTypeName}Envelope",
            LocationTypeName = locationTypeName,
        };
    }

    private string? DerivePayloadName(string responseTypeName)
    {
        var payloadName = _context.Curation.EnvelopePayloadNames.TryGetValue(_context.Operation.OperationId, out var curated)
            ? curated
            : OperationNamePolicy.PayloadName(_context.Operation);
        if (payloadName is null)
        {
            _context.Refuse("the payload name cannot be derived mechanically: the group does not pluralize naively; curate an envelope payload name");
            return null;
        }

        // Mechanical names are PascalCase by construction, but the wall covers both origins.
        if (!CSharpNamePolicy.IsValidIdentifier(payloadName))
        {
            _context.Errors.Add(
                BindingErrorCategory.Naming,
                _context.Operation.OperationId,
                $"payload name '{payloadName}' is not a valid C# identifier");
            return null;
        }

        if (ReservedNamePolicy.PayloadNames.Contains(payloadName)
            || string.Equals(payloadName, responseTypeName, StringComparison.Ordinal))
        {
            _context.Errors.Add(
                BindingErrorCategory.Naming,
                _context.Operation.OperationId,
                $"payload name '{payloadName}' collides with the response spine of '{responseTypeName}'");
            return null;
        }

        return payloadName;
    }

    private EnvelopePlan? BindNoContentEnvelope(SpecResponse success)
    {
        if (success.ContentType is not null || success.Schema is not null)
        {
            _context.Refuse("a 204 success must not carry content");
            return null;
        }

        var responseTypeName = OperationNamePolicy.ResponseTypeName(_context.Operation);
        return new EnvelopePlan
        {
            ResponseTypeName = responseTypeName,
            AdapterTypeName = $"{responseTypeName}Adapter",
            PayloadName = null,
            PayloadTypeName = null,
            Kind = EnvelopeKind.NoContent,
            SuccessStatusCode = 204,
            EnvelopeDtoTypeName = null,
        };
    }

    private string? BindBarePayload(SchemaNode schema)
    {
        return schema is RefNode reference && _context.TypeNames.TryGetValue(reference.Target, out var name)
            ? name
            : _context.RefuseNull("success payload must reference a named schema");
    }

    private string? BindDataPayload(SchemaNode schema)
    {
        if (schema is RefNode reference
            && _context.Document.Schemas.TryGetValue(reference.Target, out var target)
            && target is ObjectNode { Properties: [{ Name: "data", IsRequired: true } data] }
            && data.Schema is RefNode payload
            && _context.TypeNames.TryGetValue(payload.Target, out var name))
        {
            return name;
        }

        return _context.RefuseNull("envelope payload must be a required reference to a named schema");
    }

    private string? BindCursorListPayload(SchemaNode schema)
    {
        if (schema is not RefNode reference
            || !_context.Document.Schemas.TryGetValue(reference.Target, out var target)
            || target is not ObjectNode wrapper)
        {
            return _context.RefuseNull("cursor-list envelope must reference an object schema");
        }

        var data = wrapper.Properties.FirstOrDefault(static property => property.Name is "data");
        var cursor = wrapper.Properties.FirstOrDefault(static property => property.Name is "cursor");
        if (wrapper.Properties.Count is not 2 || data is not { IsRequired: true } || cursor is not { IsRequired: true })
        {
            return _context.RefuseNull("cursor-list envelope must require exactly 'data' and 'cursor'");
        }

        if (!SpineShapePolicy.IsListCursorShape(_context, cursor.Schema))
        {
            return _context.RefuseNull("cursor-list 'cursor' must be the optional-nullable previous/next cursor object");
        }

        // Items must reference top-level components: a promoted inline item would take its
        // name from the excluded response root, so the dialect keeps list items nominal.
        if (data.Schema is not ArrayNode { Item: RefNode item }
            || item.Target.Contains('#', StringComparison.Ordinal)
            || !_context.TypeNames.TryGetValue(item.Target, out var itemName))
        {
            return _context.RefuseNull("cursor-list 'data' must be an array of a named component schema");
        }

        return itemName;
    }

    private string? BindDataLocationPayload(SchemaNode schema)
    {
        var wrapper = ResolveDataLocationWrapper(schema);
        if (wrapper is null)
        {
            return null;
        }

        var data = wrapper.Properties.Single(static property => property.Name is "data");
        if (data.Schema is RefNode datum
            && !datum.Target.Contains('#', StringComparison.Ordinal)
            && _context.TypeNames.TryGetValue(datum.Target, out var datumName))
        {
            return datumName;
        }

        // Items must reference top-level components: a promoted inline item would take its
        // name from the excluded response root, so the dialect keeps list items nominal.
        if (data.Schema is ArrayNode { Item: RefNode item }
            && !item.Target.Contains('#', StringComparison.Ordinal)
            && _context.TypeNames.TryGetValue(item.Target, out var itemName))
        {
            return itemName;
        }

        return _context.RefuseNull("location envelope 'data' must reference a named component schema, or be an array of one");
    }

    /// <summary>
    /// The payload binder owns the wrapper walls; the sibling and kind readers resolve
    /// leniently because a malformed wrapper is already refused once.
    /// </summary>
    private ObjectNode? ResolveDataLocationWrapper(SchemaNode schema)
    {
        if (schema is not RefNode reference
            || !_context.Document.Schemas.TryGetValue(reference.Target, out var target)
            || target is not ObjectNode wrapper)
        {
            return _context.RefuseNull<ObjectNode>("location envelope must reference an object schema");
        }

        var data = wrapper.Properties.FirstOrDefault(static property => property.Name is "data");
        var location = wrapper.Properties.FirstOrDefault(static property => property.Name is "location");
        if (wrapper.Properties.Count is not 2 || data is not { IsRequired: true } || location is not { IsRequired: true })
        {
            return _context.RefuseNull<ObjectNode>("location envelope must require exactly 'data' and 'location'");
        }

        return wrapper;
    }

    private string? BindLocationSibling(SchemaNode schema)
    {
        if (schema is not RefNode reference
            || !_context.Document.Schemas.TryGetValue(reference.Target, out var target)
            || target is not ObjectNode wrapper
            || wrapper.Properties.FirstOrDefault(static property => property.Name is "location") is not { IsRequired: true } location)
        {
            return null;
        }

        // A promoted inline sibling would take its name from the excluded response root,
        // so the dialect keeps the location echo nominal.
        return location.Schema is RefNode sibling
               && !sibling.Target.Contains('#', StringComparison.Ordinal)
               && _context.TypeNames.TryGetValue(sibling.Target, out var name)
            ? name
            : _context.RefuseNull("the location sibling must reference a named component schema");
    }

    private EnvelopeKind DataLocationKind(SchemaNode schema) =>
        schema is RefNode reference
        && _context.Document.Schemas.TryGetValue(reference.Target, out var target)
        && target is ObjectNode wrapper
        && wrapper.Properties.FirstOrDefault(static property => property.Name is "data")?.Schema is ArrayNode
            ? EnvelopeKind.DataLocationList
            : EnvelopeKind.DataLocation;
}
