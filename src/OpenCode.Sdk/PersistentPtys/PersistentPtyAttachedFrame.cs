namespace OpenCode.Sdk;

/// <summary>
/// The first frame the server sends: what it granted this connection. A session consumes it
/// during <see cref="PersistentPtyClient.ConnectAsync"/> and exposes it as
/// <see cref="PersistentPtySession.Attachment"/>, so a read enumeration never yields it again.
/// </summary>
public sealed class PersistentPtyAttachedFrame : PersistentPtyFrame
{
    /// <summary>
    /// Initializes an attached frame. Public so a consumer substituting
    /// <see cref="PersistentPtySession"/> can script the frames its override yields; the SDK's own
    /// decoder uses the same door.
    /// </summary>
    /// <param name="attachment">What the server granted; never null.</param>
    public PersistentPtyAttachedFrame(PersistentPtyAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        Attachment = attachment;
    }

    /// <summary>Gets what the server granted at attach time.</summary>
    public PersistentPtyAttachment Attachment { get; }
}
