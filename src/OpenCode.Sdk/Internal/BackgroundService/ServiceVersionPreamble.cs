namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// What Error's two Discover calls decide before the election loop: reuse, throw, or enter.
/// </summary>
internal abstract record ServiceVersionPreamble
{
    private ServiceVersionPreamble()
    {
    }

    /// <summary>A service already matches the expected version; return it without entering the loop.</summary>
    /// <param name="Registration">The matching registration.</param>
    internal sealed record ReturnExisting(ServiceRegistration Registration) : ServiceVersionPreamble;

    /// <summary>A service is running at the wrong version; throw without entering the loop.</summary>
    internal sealed record ThrowMismatch : ServiceVersionPreamble;

    /// <summary>Nothing is registered; enter the election loop with the expected version.</summary>
    internal sealed record EnterLoop : ServiceVersionPreamble;
}
