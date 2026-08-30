using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

/// <summary>
/// The one definition of the wrapper shapes the name resolver and the payload binder must agree
/// on. The agreement is load-bearing, not cosmetic: <see cref="SchemaNameResolver"/> claims a
/// promoted inline payload under the operation-scoped name only for wrappers of this shape, and
/// <see cref="EnvelopeFacetBinder"/> then looks that claimed name up. Two copies of the shape
/// check could drift apart silently - the resolver naming a key the binder no longer reads, or
/// the binder reading one the resolver never claimed - and the failure would surface as a
/// mechanically-derived type name rather than as a refusal, so both sides read this instead.
/// </summary>
internal static class EnvelopeWrapperShape
{
    /// <summary>
    /// Matches the <c>{data, location}</c> wrapper: exactly two properties, both required, named
    /// <c>data</c> and <c>location</c>.
    /// </summary>
    /// <param name="wrapper">The resolved wrapper object.</param>
    /// <param name="data">The required <c>data</c> member, when the shape matched.</param>
    /// <returns>Whether the wrapper carries the data-location shape.</returns>
    public static bool IsDataLocation(ObjectNode wrapper, out SpecProperty? data)
    {
        ArgumentNullException.ThrowIfNull(wrapper);

        data = wrapper.Properties.FirstOrDefault(static property => property.Name is "data");
        var location = wrapper.Properties.FirstOrDefault(static property => property.Name is "location");
        if (wrapper.Properties.Count is 2 && data is { IsRequired: true } && location is { IsRequired: true })
        {
            return true;
        }

        data = null;
        return false;
    }
}
