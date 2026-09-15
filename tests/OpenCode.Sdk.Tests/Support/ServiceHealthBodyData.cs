namespace OpenCode.Sdk.Tests.Support;

/// <summary>
/// Health answers the background-service probe classifies, one construct each: the pinned
/// <c>{ healthy, version, pid }</c> shape and every body the probe must refuse. The daemon in these
/// bodies is pid 42 at version <c>0.0.0-test</c>, the identity <see cref="WireBodyData.HealthOk"/>
/// already carries.
/// </summary>
internal static class ServiceHealthBodyData
{
    public const int Pid = 42;

    public const string Version = "0.0.0-test";

    public const string Ready = WireBodyData.HealthOk;

    public const string Unhealthy = "{\"healthy\":false,\"version\":\"0.0.0-test\",\"pid\":42}";

    /// <summary>The pre-`version` daemon's answer; Ensure may classify it, discovery never accepts it.</summary>
    public const string Legacy = "{\"healthy\":true}";

    /// <summary>Carries `version` but no `pid`: neither the modern shape nor the legacy one.</summary>
    public const string LegacyWithVersion = "{\"healthy\":true,\"version\":\"0.0.0-test\"}";

    public const string OtherPid = "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":43}";

    public const string OtherVersion = "{\"healthy\":true,\"version\":\"0.0.0-other\",\"pid\":42}";

    /// <summary>The literal the server reports when the app carries no version; compared as an ordinary string.</summary>
    public const string UnknownVersion = "{\"healthy\":true,\"version\":\"unknown\",\"pid\":42}";

    /// <summary>Decodes into the generated model's Int64 pid, then fails the Int32 narrowing.</summary>
    public const string PidAboveInt32 = "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":2147483648}";

    /// <summary>Does not decode at all: the number exceeds Int64, which the serializer reports as malformed.</summary>
    public const string PidAboveInt64 = "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":9223372036854775808}";

    public const string Malformed = "{\"healthy\":true,\"version\":\"0.0.0-test\",\"pid\":42";

    public const string ArrayRoot = "[]";
}
