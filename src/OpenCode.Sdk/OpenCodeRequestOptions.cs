namespace OpenCode.Sdk;

/// <summary>Configures one opencode API call.</summary>
public sealed class OpenCodeRequestOptions
{
    /// <summary>Gets the error channel for this call.</summary>
    public ErrorBehavior ErrorBehavior { get; init; }

    /// <summary>
    /// Gets the per-call location override. Unset members inherit the ambient
    /// <see cref="OpenCodeClientOptions.Location"/> member-by-member; a set member always wins
    /// over its ambient counterpart. <see langword="null"/> uses the ambient location
    /// unmodified. Because <see cref="LocationSelector"/> refuses blank members, there is no way
    /// to clear an ambient member for one call — only to leave it inherited or replace it.
    /// </summary>
    public LocationSelector? Location { get; init; }

    /// <summary>Gets a shared instance that returns API errors on the response envelope.</summary>
    public static OpenCodeRequestOptions NoThrow { get; } = new()
    {
        ErrorBehavior = ErrorBehavior.NoThrow,
    };
}
