using System.Text;
using OpenCode.Sdk.Tools.Generator.Emission;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class EmitterSnapshot
{
    public static string Create(IReadOnlyList<GeneratedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var result = new StringBuilder();
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            _ = result.Append("// File: ").Append(source.RelativePath).Append('\n');
            _ = result.Append(Encoding.UTF8.GetString(source.Utf8Source.Span));
            if (index < sources.Count - 1)
            {
                _ = result.Append('\n');
            }
        }

        return result.ToString();
    }
}
