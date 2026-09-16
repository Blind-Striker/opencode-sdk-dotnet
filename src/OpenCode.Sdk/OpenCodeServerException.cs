namespace OpenCode.Sdk;

/// <summary>
/// A local server door failed: a standalone server could not start, report readiness, or stop,
/// or background-service discovery could not resolve the user home its registration roots hang
/// off. An absent or unusable registration is not a failure; discovery reports it as null.
/// </summary>
public class OpenCodeServerException : OpenCodeException
{
    /// <summary>Initializes a new instance of the <see cref="OpenCodeServerException"/> class.</summary>
    public OpenCodeServerException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OpenCodeServerException"/> class.</summary>
    /// <param name="message">The failure description.</param>
    public OpenCodeServerException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OpenCodeServerException"/> class.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OpenCodeServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
