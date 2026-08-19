using OpenCode.Sdk.Models;

namespace OpenCode.Sdk;

/// <summary>Represents a schema-valid failure reported after an opencode event stream opened.</summary>
public sealed class OpenCodeStreamFailureException : OpenCodeTransportException
{
    private static readonly IReadOnlyList<IOpenCodeStreamFailureCause> EmptyCause =
        Array.AsReadOnly(Array.Empty<IOpenCodeStreamFailureCause>());

    /// <summary>Initializes an exception with an empty cause.</summary>
    public OpenCodeStreamFailureException()
    {
        Cause = EmptyCause;
    }

    /// <summary>Initializes an exception with a message and an empty cause.</summary>
    /// <param name="message">The failure description.</param>
    public OpenCodeStreamFailureException(string message)
        : base(message)
    {
        Cause = EmptyCause;
    }

    /// <summary>Initializes an exception with a message, underlying failure, and empty cause.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OpenCodeStreamFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        Cause = EmptyCause;
    }

    /// <summary>Initializes an exception with the cause reported by the stream.</summary>
    /// <param name="cause">The typed reasons reported by the reserved failure frame.</param>
    public OpenCodeStreamFailureException(IReadOnlyList<IOpenCodeStreamFailureCause> cause)
        : base("The opencode event stream reported a failure after it opened.")
    {
        ArgumentNullException.ThrowIfNull(cause);
        Cause = cause;
    }

    /// <summary>Gets the typed reasons reported by the stream.</summary>
    public IReadOnlyList<IOpenCodeStreamFailureCause> Cause { get; }
}
