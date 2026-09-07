using System.Reflection;
using System.Text;

namespace OpenCode.Sdk.TestSupport;

/// <summary>Loads embedded fixtures from the compiling test assembly's Fixtures folder.</summary>
internal sealed class FixtureLoader
{
    private readonly Assembly _assembly = typeof(FixtureLoader).Assembly;

    public string LoadJson(string name) => LoadText(name);

    public string LoadText(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var resourceName = string.Concat(_assembly.GetName().Name, ".Fixtures.", name);
        var stream = _assembly.GetManifestResourceStream(resourceName)
                     ?? throw new ArgumentException($"Embedded fixture '{name}' was not found.", nameof(name));

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var content = Encoding.UTF8.GetString(reader.ReadBytes(checked((int)stream.Length)));
        return content.TrimEnd('\r', '\n');
    }
}
