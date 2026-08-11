namespace OpenCode.Sdk.Tools.Generator.Emission;

internal sealed record GeneratedSource
{
    public required string RelativePath
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required ReadOnlyMemory<byte> Utf8Source
    {
        get;
        init => field = value.ToArray();
    }
}
