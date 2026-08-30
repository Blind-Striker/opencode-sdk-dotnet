using System.Diagnostics;

namespace OpenCode.Sdk.Internal;

/// <summary>Escapes URI components identically across modern and downlevel target frameworks.</summary>
internal static class RouteValuePolicy
{
    private const int MaximumInputLength = 32_766;

    /// <summary>Refuses legacy-incompatible inputs before delegating to the platform escaper.</summary>
    public static string Escape(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        Debug.Assert(!string.IsNullOrWhiteSpace(parameterName));

        if (value.Length > MaximumInputLength)
        {
            throw new ArgumentException($"Route values must not exceed {MaximumInputLength} UTF-16 code units.", parameterName);
        }

        if (!HasValidUtf16(value))
        {
            throw new ArgumentException("Route values must contain valid UTF-16.", parameterName);
        }

        return Uri.EscapeDataString(value);
    }

    /// <summary>
    /// Escapes a value that becomes one path segment. Escaping leaves a dot segment intact and
    /// <see cref="Uri"/> then resolves it away, silently addressing a different route than the
    /// caller named, so the refusal belongs here rather than at every call site that composes a
    /// path. A query value takes <see cref="Escape"/> instead: a dot is an ordinary value there.
    /// </summary>
    public static string EscapeSegment(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value is "." or "..")
        {
            throw new ArgumentException("Route values must not be dot segments.", parameterName);
        }

        return Escape(value, parameterName);
    }

    /// <summary>Escapes an SDK-owned wire name whose representability is an internal invariant.</summary>
    public static string EscapeName(string value)
    {
        Debug.Assert(value.Length <= MaximumInputLength);
        Debug.Assert(HasValidUtf16(value));
        return Uri.EscapeDataString(value);
    }

    private static bool HasValidUtf16(string value)
    {
        var index = 0;
        while (index < value.Length)
        {
            var character = value[index];
            if (!char.IsSurrogate(character))
            {
                index++;
                continue;
            }

            if (!char.IsHighSurrogate(character) || index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
            {
                return false;
            }

            index += 2;
        }

        return true;
    }
}
