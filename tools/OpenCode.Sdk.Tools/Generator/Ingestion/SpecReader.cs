using System.IO.Abstractions;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace OpenCode.Sdk.Tools.Generator.Ingestion;

internal sealed class SpecReader(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<LoadedSpec> LoadAsync(string specPath, IngestionErrorCollector errors, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specPath);
        ArgumentNullException.ThrowIfNull(errors);

        if (!_fileSystem.File.Exists(specPath))
        {
            errors.Add("document", $"spec file was not found — {specPath}");
            errors.ThrowIfAny();
        }

        var stream = _fileSystem.File.OpenRead(specPath);
        await using (stream.ConfigureAwait(false))
        {
            var settings = new OpenApiReaderSettings
            {
                LeaveStreamOpen = true,
            };

            ReadResult result;
            JsonNode raw;
            try
            {
                result = await OpenApiDocument.LoadAsync(stream, "json", settings, ct).ConfigureAwait(false);
                stream.Position = 0;
                raw = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("The OpenAPI reader accepted an empty JSON document.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add("document", $"the reader failed — {exception.GetType().Name}: {exception.Message}");
                errors.ThrowIfAny();
                throw;
            }

            var diagnostic = result.Diagnostic
                             ?? throw new InvalidOperationException("The OpenAPI reader returned no diagnostic information.");

            foreach (var error in diagnostic.Errors)
            {
                errors.Add(string.IsNullOrEmpty(error.Pointer) ? "document" : error.Pointer, error.Message);
            }

            if (diagnostic.SpecificationVersion is not OpenApiSpecVersion.OpenApi3_1)
            {
                var declaredVersion = raw["openapi"]?.GetValue<string>() ?? diagnostic.SpecificationVersion.ToString();
                errors.Add("document", $"OpenAPI specification version '{declaredVersion}' is not supported; expected 3.1.");
            }

            errors.ThrowIfAny();
            var document = result.Document
                           ?? throw new InvalidOperationException("The OpenAPI reader returned no document without a diagnostic error.");
            return new LoadedSpec(document, raw);
        }
    }
}
