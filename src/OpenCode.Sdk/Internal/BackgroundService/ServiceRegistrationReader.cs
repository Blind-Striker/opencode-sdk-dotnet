using System.Text.Json;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The strict hand-written boundary over the registration file (<c>Service.Info</c> at the pin:
/// optional <c>id</c> and <c>version</c> strings, required <c>url</c>, required positive
/// <c>pid</c>, optional <c>password</c>). A <c>Utf8JsonReader</c> pass, no serializer context, no
/// type that could print the password. Every invalid document is absent state, the way upstream's
/// <c>read</c> folds a decode failure into "no registration"; the absolute-URL, Int32-pid, and
/// duplicate-member rules are the SDK's own, stricter than the JSON.parse upstream decodes with.
/// </summary>
internal static class ServiceRegistrationReader
{
    /// <summary>Decodes a registration document.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <returns>The registration, or null for any document the daemon could not have written.</returns>
    public static ServiceRegistration? TryRead(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return Read(utf8);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ServiceRegistration? Read(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        var fields = new Fields();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var member = Identify(ref reader);
            if (member == Members.None)
            {
                reader.Skip();
                continue;
            }

            if (!fields.Take(member) || !fields.ReadMember(ref reader, member))
            {
                return null;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            return null;
        }

        return fields.ToRegistration();
    }

    private static Members Identify(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("id"u8))
        {
            return Members.Id;
        }

        if (reader.ValueTextEquals("version"u8))
        {
            return Members.Version;
        }

        if (reader.ValueTextEquals("url"u8))
        {
            return Members.Url;
        }

        if (reader.ValueTextEquals("pid"u8))
        {
            return Members.Pid;
        }

        return reader.ValueTextEquals("password"u8) ? Members.Password : Members.None;
    }

    [Flags]
    private enum Members
    {
        None = 0,
        Id = 1,
        Version = 2,
        Url = 4,
        Pid = 8,
        Password = 16,
    }

    private struct Fields
    {
        private Members _seen;
        private string? _id;
        private string? _version;
        private string? _url;
        private string? _password;
        private int _processId;

        public bool Take(Members member)
        {
            if ((_seen & member) != Members.None)
            {
                return false;
            }

            _seen |= member;
            return true;
        }

        public bool ReadMember(ref Utf8JsonReader reader, Members member) =>
            member switch
            {
                Members.Id => ReadString(ref reader, out _id),
                Members.Version => ReadString(ref reader, out _version),
                Members.Url => ReadString(ref reader, out _url),
                Members.Password => ReadString(ref reader, out _password),
                Members.Pid => ReadPid(ref reader),
                Members.None => false,
                _ => false,
            };

        public readonly ServiceRegistration? ToRegistration()
        {
            if (_url is null || (_seen & Members.Pid) == Members.None ||
                !Uri.TryCreate(_url, UriKind.Absolute, out var endpoint) || !IsHttp(endpoint))
            {
                return null;
            }

            return new ServiceRegistration(
                _id, _version, _url, endpoint, _processId, string.IsNullOrWhiteSpace(_password) ? null : _password);
        }

        private static bool ReadString(ref Utf8JsonReader reader, out string? value)
        {
            value = null;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                return false;
            }

            value = reader.GetString();
            return true;
        }

        private static bool IsHttp(Uri endpoint) =>
            string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

        private bool ReadPid(ref Utf8JsonReader reader)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number ||
                !reader.TryGetInt32(out var pid) || pid <= 0)
            {
                return false;
            }

            _processId = pid;
            return true;
        }
    }
}
