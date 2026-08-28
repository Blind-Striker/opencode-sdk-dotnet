namespace OpenCode.Sdk;

/// <summary>The standalone server process failed to start, report readiness, or stop.</summary>
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
