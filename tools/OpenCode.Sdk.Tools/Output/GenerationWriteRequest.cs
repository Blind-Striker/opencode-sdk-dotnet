using System.Collections.ObjectModel;
using OpenCode.Sdk.Tools.Generator.Emission;

namespace OpenCode.Sdk.Tools.Output;

/// <summary>One generation write: the sources, their admitted family folders, and the run flags.</summary>
internal sealed record GenerationWriteRequest
{
    public required string OutputRoot
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required string ProjectPath
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required IReadOnlyList<GeneratedSource> Sources
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    }

    /// <summary>Gets the client-family folders new sources may live in, derived from the plan's container names.</summary>
    public required IReadOnlyList<string> FamilyFolders
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    }

    /// <summary>Gets the stabilize duplicates the binder folded mechanically, recorded in the manifest as a telltale.</summary>
    public required IReadOnlyDictionary<string, string> ImplicitSchemaAliases
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
        }
    }

    /// <summary>Gets the partial-generation marker content, or <see langword="null"/> when generation is complete.</summary>
    public string? PartialMarkerContent
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
            }

            field = value;
        }
    }

    public required bool Verify { get; init; }
}
