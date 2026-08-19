namespace OpenCode.Sdk.Tools.Tests.Support;

internal enum StreamExtensionProfile
{
    Valid = 0,
    MissingEncoding = 1,
    UnsupportedEncoding = 2,
    MessageFailure = 3,
    NonObject = 4,
    MissingCauseSchema = 5,
    NonNeverErrorSchema = 6,
    MissingErrorSchema = 7,
}
