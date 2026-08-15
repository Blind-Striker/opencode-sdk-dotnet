using System.Collections.ObjectModel;

namespace OpenCode.Sdk.Internal.Serialization;

/// <summary>Copies a list payload into the envelope, refusing null elements the wire contract forbids.</summary>
internal static class ListPayloadInput
{
    internal static ReadOnlyCollection<T> CopyRejectingNullElements<T>(IReadOnlyList<T> value)
        where T : class
    {
        var copy = new T[value.Count];
        for (var index = 0; index < value.Count; index++)
        {
            copy[index] = value[index]
                          ?? throw new ArgumentException("The list payload cannot contain a null element.", nameof(value));
        }

        return Array.AsReadOnly(copy);
    }
}
