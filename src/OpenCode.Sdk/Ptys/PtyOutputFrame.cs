namespace OpenCode.Sdk;

/// <summary>
/// Terminal output from the pseudo-terminal. The server chunks its retained replay at 64Ki UTF-16
/// code units, so a chunk boundary can split a surrogate pair; the text is therefore decoded with
/// replacement rather than refused, and a caller that concatenates consecutive frames sees the
/// stream the terminal produced.
/// </summary>
public sealed class PtyOutputFrame : PtyFrame
{
    /// <summary>
    /// Initializes an output frame. Public so a consumer substituting <see cref="PtySession"/>
    /// can script the frames its override yields; the SDK's own reader uses the same door.
    /// </summary>
    /// <param name="text">The output the frame carries.</param>
    public PtyOutputFrame(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
    }

    /// <summary>Gets the decoded output this frame carries.</summary>
    public string Text { get; }
}
