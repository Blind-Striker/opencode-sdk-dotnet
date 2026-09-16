namespace OpenCode.Sdk.Tests.BackgroundService;

/// <summary>
/// Service-config documents that isolate one construct each: the five members the pinned CLI's
/// <c>ServiceConfig.Info</c> declares and the rejections the strict reader owns. The canonical
/// document with an environment map is the embedded fixture
/// <c>BackgroundService.service-config-env.json</c>.
/// </summary>
internal static class ServiceConfigData
{
    public const string ConfigPassword = "c0nf1g-p455w0rd";

    public const string Empty = "{}";

    public const string HostnameOnly = "{\"hostname\":\"0.0.0.0\"}";

    public const string PortAtLowerBound = "{\"port\":1}";

    public const string PortAtUpperBound = "{\"port\":65535}";

    public const string PortZero = "{\"port\":0}";

    public const string PortAboveRange = "{\"port\":65536}";

    public const string PortAsString = "{\"port\":\"49374\"}";

    public const string CorsNotAnArray = "{\"cors\":\"http://localhost:5173\"}";

    public const string CorsWithNonString = "{\"cors\":[\"http://localhost:5173\",5173]}";

    public const string EnvWithNonStringValue = "{\"env\":{\"HTTPS_PROXY\":3128}}";

    public const string EnvNotAnObject = "{\"env\":[\"HTTPS_PROXY=x\"]}";

    public const string HostnameNotAString = "{\"hostname\":1}";

    public const string ArrayRoot = "[]";

    public const string Malformed = "{\"env\":{";

    public const string UnknownMembersSkipped = "{\"env\":{\"A\":\"1\"},\"future\":{\"nested\":true}}";
}
