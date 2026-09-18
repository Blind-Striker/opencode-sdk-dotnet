namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Info answers the background-service probe classifies, one construct each: the pinned
/// <c>{ version, pid, urls, paths }</c> body a daemon sends and every body the probe must refuse. The
/// probe reads only <c>version</c> and <c>pid</c>, as the pinned client does. The daemon in these
/// bodies is pid 42 at version <c>0.0.0-test</c>, the identity used by the probe fixtures.
/// </summary>
internal static class ServiceInfoBodyData
{
    public const int Pid = 42;

    public const string Version = "0.0.0-test";

    private const string Urls = "\"urls\":[\"http://127.0.0.1:4096\"],\"paths\":{\"tmp\":\"/tmp/opencode\"}";

    public const string Ready = "{\"version\":\"0.0.0-test\",\"pid\":42," + Urls + "}";

    /// <summary>An unknown member and a <c>urls</c> the public model would refuse; neither is identity.</summary>
    public const string IgnoredFields = "{\"extra\":false,\"urls\":false,\"version\":\"0.0.0-test\",\"pid\":42}";

    /// <summary>An object without the required identity fields.</summary>
    public const string MissingIdentity = "{" + Urls + "}";

    /// <summary>Carries `version` but no `pid`.</summary>
    public const string MissingPid = "{\"version\":\"0.0.0-test\"," + Urls + "}";

    public const string OtherPid = "{\"version\":\"0.0.0-test\",\"pid\":43," + Urls + "}";

    public const string OtherVersion = "{\"version\":\"0.0.0-other\",\"pid\":42," + Urls + "}";

    /// <summary>The literal the server reports when the app carries no version; compared as an ordinary string.</summary>
    public const string UnknownVersion = "{\"version\":\"unknown\",\"pid\":42," + Urls + "}";

    /// <summary>Exceeds the process identity range.</summary>
    public const string PidAboveInt32 = "{\"version\":\"0.0.0-test\",\"pid\":2147483648," + Urls + "}";

    /// <summary>Exceeds both Int32 and Int64.</summary>
    public const string PidAboveInt64 = "{\"version\":\"0.0.0-test\",\"pid\":9223372036854775808," + Urls + "}";

    public const string Malformed = "{\"version\":\"0.0.0-test\",\"pid\":42," + Urls;

    public const string InvalidVersionUnicode = "{\"version\":\"\\uD800\",\"pid\":42," + Urls + "}";

    public const string ArrayRoot = "[]";
}
