using System.Runtime.CompilerServices;
using System.Text;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Reads one response body as a server-sent event stream and yields each frame's data
/// payload. Only the <c>data</c> field carries a payload for this client, so every other
/// field is skipped and a frame carrying none never yields. One instance reads one body.
/// </summary>
internal sealed class ServerSentEventReader
{
    /// <summary>The pending-frame ceiling; a stream that never closes a frame cannot grow without bound.</summary>
    public const int DefaultMaxFrameCharacters = 16 * 1024 * 1024;

    private const string DataField = "data:";
    private const int ReadBufferBytes = 8192;

    private readonly StringBuilder _data = new();
    private readonly StringBuilder _line = new();
    private readonly int _maxFrameCharacters;
    private int _frameCharacters;
    private bool _sawCarriageReturn;

    public ServerSentEventReader()
        : this(DefaultMaxFrameCharacters)
    {
    }

    public ServerSentEventReader(int maxFrameCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameCharacters, 1);

        _maxFrameCharacters = maxFrameCharacters;
    }

    public IAsyncEnumerable<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        // The guard runs on the call, not on the first enumeration.
        ArgumentNullException.ThrowIfNull(stream);

        return ReadCoreAsync(stream, cancellationToken);
    }

    private async IAsyncEnumerable<string> ReadCoreAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The decoder carries a partial multi-byte character across reads, so a chunk
        // boundary inside one character never corrupts it.
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[ReadBufferBytes];
        var characters = new char[Encoding.UTF8.GetMaxCharCount(ReadBufferBytes)];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read is 0)
            {
                break;
            }

            var decoded = decoder.GetChars(bytes, 0, read, characters, 0);
            for (var index = 0; index < decoded; index++)
            {
                if (Accept(characters[index]) is { } payload)
                {
                    yield return payload;
                }
            }
        }

        // A body may end without the blank line that closes its last frame.
        if (CompleteLine() is { } trailing)
        {
            yield return trailing;
        }
        else if (TakeFrame() is { } pending)
        {
            yield return pending;
        }
    }

    /// <summary>Feeds one character through the line state machine, returning a completed frame.</summary>
    private string? Accept(char character)
    {
        // A CR already terminated the line, so the LF completing a CRLF pair is not a second terminator.
        if (character is '\n' && _sawCarriageReturn)
        {
            _sawCarriageReturn = false;
            return null;
        }

        _sawCarriageReturn = character is '\r';
        if (character is '\r' or '\n')
        {
            return CompleteLine();
        }

        Grow(1);
        _ = _line.Append(character);
        return null;
    }

    /// <summary>Closes the pending line: an empty one ends the frame, a data line extends it.</summary>
    private string? CompleteLine()
    {
        if (_line.Length is 0)
        {
            return TakeFrame();
        }

        if (StartsWithDataField())
        {
            AppendDataValue();
        }

        _ = _line.Clear();
        return null;
    }

    private string? TakeFrame()
    {
        if (_data.Length is 0)
        {
            _frameCharacters = 0;
            return null;
        }

        var payload = _data.ToString();
        _ = _data.Clear();
        _frameCharacters = 0;
        return payload;
    }

    private void AppendDataValue()
    {
        // The field separator is followed by at most one optional space, and only that
        // one space belongs to the framing rather than the payload.
        var start = DataField.Length;
        if (start < _line.Length && _line[start] is ' ')
        {
            start++;
        }

        if (_data.Length > 0)
        {
            Grow(1);
            _ = _data.Append('\n');
        }

        // Appending the builder segment directly keeps the line out of an intermediate string.
        _ = _data.Append(_line, start, _line.Length - start);
    }

    private bool StartsWithDataField()
    {
        if (_line.Length < DataField.Length)
        {
            return false;
        }

        for (var index = 0; index < DataField.Length; index++)
        {
            if (_line[index] != DataField[index])
            {
                return false;
            }
        }

        return true;
    }

    private void Grow(int characters)
    {
        _frameCharacters += characters;
        if (_frameCharacters > _maxFrameCharacters)
        {
            throw new OpenCodeTransportException(
                "The opencode event stream produced a frame beyond the size this client accepts.");
        }
    }
}
