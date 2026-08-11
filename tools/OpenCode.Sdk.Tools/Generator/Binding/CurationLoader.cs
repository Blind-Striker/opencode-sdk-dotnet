using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class CurationLoader(IFileSystem fileSystem)
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<GenerationCuration> LoadAsync(string curationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(curationPath);

        if (!_fileSystem.File.Exists(curationPath))
        {
            throw CreateException(curationPath, "curation file was not found");
        }

        var stream = _fileSystem.File.OpenRead(curationPath);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<GenerationCuration>(stream, SerializerOptions, cancellationToken)
                           .ConfigureAwait(false)
                       ?? throw CreateException(curationPath, "curation document cannot be JSON null");
            }
            catch (JsonException exception)
            {
                throw CreateException(curationPath, exception.Message);
            }
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter<GroupPlacement>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<PropertyOverrideType>(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static BindingException CreateException(string subject, string problem) =>
        new(Array.AsReadOnly([new BindingError(BindingErrorCategory.Curation, subject, problem)]));
}
