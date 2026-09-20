using System.Globalization;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Composes the registration document a daemon would have published, so a test that seeds one for
/// a real process states the identity it names rather than pasting a JSON dump.
/// </summary>
internal static class ServiceRegistrationDocument
{
    public static string Compose(string id, string version, Uri endpoint, int processId, string password)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return "{\"id\":\"" + id + "\",\"version\":\"" + version + "\",\"url\":\"" + endpoint
            + "\",\"pid\":" + processId.ToString(CultureInfo.InvariantCulture)
            + ",\"password\":\"" + password + "\"}";
    }
}
