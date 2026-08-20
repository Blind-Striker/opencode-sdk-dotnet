using System.Globalization;
using System.Text;

namespace OpenCode.Sdk.Performance.Tests.Support;

/// <summary>
/// Derives the benchmark's message variants from the one deep assistant-message seed, so every
/// variant differs from the seed in exactly the dimension it isolates: where the union marker
/// sits, whether it repeats, whether it is known, and how many content parts the message carries.
/// </summary>
internal sealed class AssistantMessageComposer
{
    private const string EarlyMarker = "\"type\":\"assistant\",";
    private const string LateMarker = ",\"type\":\"assistant\"";
    private const string SeedId = "msg_bench00000000000000000001";

    /// <summary>The number of content parts the seed carries.</summary>
    private const int SeedParts = 4;
    private static readonly byte[] ContentStart = "\"content\":["u8.ToArray();
    private static readonly byte[] ContentEnd = "],\"metadata\""u8.ToArray();

    private readonly byte[] _seed;
    private readonly int _partsStart;
    private readonly int _partsEnd;

    public AssistantMessageComposer(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _seed = seed;
        _partsStart = IndexOf(seed, ContentStart, 0) + ContentStart.Length;
        _partsEnd = IndexOf(seed, ContentEnd, _partsStart);
        if (IndexOf(seed, Encoding.UTF8.GetBytes(EarlyMarker), 0) < 0 || IndexOf(seed, Encoding.UTF8.GetBytes(SeedId), 0) < 0)
        {
            throw new InvalidOperationException("The deep assistant-message seed no longer carries the expected marker and id.");
        }
    }

    /// <summary>The seed unchanged: the marker is the second property, ahead of every nested union.</summary>
    public byte[] MarkerEarly() => _seed;

    /// <summary>Moves the marker to the last property, so the discriminator scan walks the whole payload first.</summary>
    public byte[] MarkerLast()
    {
        var text = Encoding.UTF8.GetString(_seed);
        var index = text.IndexOf(EarlyMarker, StringComparison.Ordinal);
        var without = text.Remove(index, EarlyMarker.Length);
        return Encoding.UTF8.GetBytes(without.Insert(without.Length - 1, LateMarker));
    }

    /// <summary>Keeps an early foreign marker and adds the real one last, so only the last-value rule selects the known variant.</summary>
    public byte[] DuplicateMarkerLastKnown()
    {
        var text = Encoding.UTF8.GetString(_seed);
        var index = text.IndexOf(EarlyMarker, StringComparison.Ordinal);
        var replaced = text.Remove(index, EarlyMarker.Length).Insert(index, "\"type\":\"user\",");
        return Encoding.UTF8.GetBytes(replaced.Insert(replaced.Length - 1, LateMarker));
    }

    /// <summary>Renames the marker to a value no generated variant owns, so the unknown carrier retains the DOM.</summary>
    public byte[] UnknownMarker()
    {
        var text = Encoding.UTF8.GetString(_seed);
        var index = text.IndexOf(EarlyMarker, StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(text.Remove(index, EarlyMarker.Length).Insert(index, "\"type\":\"assistant-v2\","));
    }

    /// <summary>Repeats the seed's content parts so one message carries the requested part count.</summary>
    public byte[] WithContentParts(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, SeedParts);
        if (parts % SeedParts is not 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "The part count must be a multiple of the seed's parts.");
        }

        var seedParts = _seed.AsSpan(_partsStart, _partsEnd - _partsStart);
        using var buffer = new MemoryStream();
        buffer.Write(_seed.AsSpan(0, _partsStart));
        for (var repeat = 0; repeat < parts / SeedParts; repeat++)
        {
            if (repeat > 0)
            {
                buffer.WriteByte((byte)',');
            }

            buffer.Write(seedParts);
        }

        buffer.Write(_seed.AsSpan(_partsEnd));
        return buffer.ToArray();
    }

    /// <summary>Produces distinct-id copies of the seed, the items a cursor-list page carries.</summary>
    public IReadOnlyList<byte[]> Page(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var text = Encoding.UTF8.GetString(_seed);
        var items = new byte[count][];
        for (var index = 0; index < count; index++)
        {
            var id = "msg_bench" + (index + 1).ToString("D20", CultureInfo.InvariantCulture);
            items[index] = Encoding.UTF8.GetBytes(new StringBuilder(text).Replace(SeedId, id).ToString());
        }

        return items;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        var index = haystack.AsSpan(start).IndexOf(needle);
        return index < 0 ? -1 : index + start;
    }
}
