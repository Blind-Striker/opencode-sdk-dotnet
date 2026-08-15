namespace OpenCode.Sdk;

/// <summary>
/// Selects the directory and workspace a request addresses; unset members leave the
/// server's own resolution in place. On a request it rides the wire as
/// <c>location[directory]</c> and <c>location[workspace]</c> query keys; as the client
/// options' ambient default it rides the <c>x-opencode-directory</c> and
/// <c>x-opencode-workspace</c> headers, which the server resolves after any query value.
/// </summary>
public sealed record LocationSelector
{
    private readonly string? _directory;
    private readonly string? _workspace;

    /// <summary>Gets the directory the request addresses; a blank value is refused.</summary>
    public string? Directory
    {
        get => _directory;
        init
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "The location directory cannot be empty or whitespace; leave it null when unset.",
                    nameof(value));
            }

            _directory = value;
        }
    }

    /// <summary>Gets the workspace the request addresses; a blank value is refused.</summary>
    public string? Workspace
    {
        get => _workspace;
        init
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "The location workspace cannot be empty or whitespace; leave it null when unset.",
                    nameof(value));
            }

            _workspace = value;
        }
    }
}
