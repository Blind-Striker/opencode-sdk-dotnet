namespace OpenCode.Sdk;

/// <summary>
/// Terminal output from the pseudo-terminal. The server chunks its retained replay at 64Ki UTF-16
/// code units, so a chunk boundary can split a surrogate pair; the text is therefore decoded with
/// replacement rather than refused, and a caller that concatenates consecutive frames sees the
/// stream the terminal produced.
/// </summary>
public sealed class PtyOutputFrame : PtyFrame
{
    internal PtyOutputFrame(string text)
    {
        Text = text;
    }

    /// <summary>Gets the decoded output this frame carries.</summary>
    public string Text { get; }
}
