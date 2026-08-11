using System.Reflection;
using System.Text;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class FixtureLoader
{
    private const string ResourcePrefix = "OpenCode.Sdk.Tools.Tests.Fixtures.";

    private readonly Assembly _assembly = typeof(FixtureLoader).Assembly;

    public string Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resourceName = ResourcePrefix + name;
        var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            var knownNames = GetKnownNames();
            var knownNamesList = knownNames.Length == 0
                ? "<none>"
                : string.Join(", ", knownNames);
            throw new ArgumentException($"Embedded fixture '{name}' was not found. Known fixtures: {knownNamesList}.", nameof(name));
        }

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var content = Encoding.UTF8.GetString(reader.ReadBytes(checked((int)stream.Length)));
        return content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;
    }

    private string[] GetKnownNames() =>
    [
        .. _assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..])
            .Order(StringComparer.Ordinal),
    ];
}
