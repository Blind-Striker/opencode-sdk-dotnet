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
