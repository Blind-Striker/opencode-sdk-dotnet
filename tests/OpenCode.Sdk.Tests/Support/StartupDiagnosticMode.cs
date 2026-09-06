namespace OpenCode.Sdk.Tests.Support;

public enum StartupDiagnosticMode
{
    InvalidReadiness,
    ReadinessTimeout,
    CallerCancellation,
    EarlyExit,
}
