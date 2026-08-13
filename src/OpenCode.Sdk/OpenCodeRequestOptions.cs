namespace OpenCode.Sdk;

/// <summary>Configures one opencode API call.</summary>
public sealed class OpenCodeRequestOptions
{
    /// <summary>Gets the error channel for this call.</summary>
    public ErrorBehavior ErrorBehavior { get; init; }

    /// <summary>Gets a shared instance that returns API errors on the response envelope.</summary>
    public static OpenCodeRequestOptions NoThrow { get; } = new()
    {
        ErrorBehavior = ErrorBehavior.NoThrow,
    };
}
