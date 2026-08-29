using System.Globalization;
using System.Text;

namespace OpenCode.Sdk.Sandbox;

/// <summary>
/// Renders raw terminal output as printable console evidence. Both PTY walkthroughs print the
/// same way - terminal bytes carry escape sequences and newlines that would otherwise wreck the
/// console transcript, and a marker is only convincing when the noise around it is visible - so
/// the rendering lives here once rather than once per family.
/// </summary>
internal static class TerminalExcerpt
{
    /// <summary>How much raw terminal output is printed on either side of the marker.</summary>
    private const int ContextWidth = 24;

    /// <summary>Keeps a printed terminal excerpt to one readable console line.</summary>
    private const int ExcerptLimit = 160;

    /// <summary>
    /// Shows the marker in the surrounding terminal noise, so the match is visible rather than
    /// asserted. A stretch that does not carry it - the terminal is free to finish the round trip
    /// inside either read - prints from its start instead.
    /// </summary>
    /// <param name="text">The terminal text to render.</param>
    /// <param name="marker">The text being looked for.</param>
    /// <returns>The rendered excerpt, prefixed with the marker's offset when it was found.</returns>
    public static string Around(string text, string marker)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(marker);

        var index = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return Excerpt(text);
        }

        var start = Math.Max(0, index - ContextWidth);
        var length = Math.Min(text.Length - start, marker.Length + (2 * ContextWidth));
        return string.Create(CultureInfo.InvariantCulture, $"@{index} {Excerpt(text.Substring(start, length))}");
    }

    /// <summary>Renders terminal bytes as one printable line; escapes keep the console readable.</summary>
    /// <param name="text">The terminal text to render.</param>
    /// <returns>One printable line, truncated at the excerpt limit.</returns>
    public static string Excerpt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (builder.Length >= ExcerptLimit)
            {
                _ = builder.Append('…');
                break;
            }

            _ = character switch
            {
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                _ when char.IsControl(character) => builder.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:x2}"),
                _ => builder.Append(character),
            };
        }

        return builder.ToString();
    }
}
