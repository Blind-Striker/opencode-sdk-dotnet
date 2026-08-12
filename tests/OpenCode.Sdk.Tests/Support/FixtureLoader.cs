using System.Reflection;
using System.Text;

namespace OpenCode.Sdk.Tests.Support;

internal sealed class FixtureLoader
{
    private const string ResourcePrefix = "OpenCode.Sdk.Tests.Fixtures.";
    private readonly Assembly _assembly = typeof(FixtureLoader).Assembly;

    public string LoadJson(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var resourceName = string.Concat(ResourcePrefix, name);
        var stream = _assembly.GetManifestResourceStream(resourceName)
                     ?? throw new ArgumentException($"Embedded JSON fixture '{name}' was not found.", nameof(name));

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var content = Encoding.UTF8.GetString(reader.ReadBytes(checked((int)stream.Length)));
        return content.TrimEnd('\r', '\n');
    }
}
