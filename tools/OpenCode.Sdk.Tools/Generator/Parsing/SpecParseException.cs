using System.Globalization;

namespace OpenCode.Sdk.Tools.Generator.Parsing;

/// <summary>Batched parser refusal: every collected dialect or structure error in one throw.</summary>
public sealed class SpecParseException : Exception
{
    /// <summary>Creates the exception from the batched parse errors.</summary>
    public SpecParseException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Standard constructor; carries no batched errors.</summary>
    public SpecParseException()
    {
        Errors = [];
    }

    /// <summary>Standard constructor; carries no batched errors.</summary>
    public SpecParseException(string message)
        : base(message)
    {
        Errors = [];
    }

    /// <summary>Standard constructor; carries no batched errors.</summary>
    public SpecParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }

    /// <summary>The batched parse errors, in document order.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0
            ? "Spec parse failed."
            : string.Create(CultureInfo.InvariantCulture, $"Spec parse failed with {errors.Count} error(s):\n{string.Join('\n', errors)}");
    }
}
