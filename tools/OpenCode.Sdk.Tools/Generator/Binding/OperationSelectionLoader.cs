using System.IO.Abstractions;
using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class OperationSelectionLoader(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<OperationSelection> LoadAsync(string profilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

        var errors = new BindingErrorCollector();
        if (!_fileSystem.File.Exists(profilePath))
        {
            errors.Add(BindingErrorCategory.Selection, profilePath, "generation profile was not found");
            errors.ThrowIfAny();
        }

        var lines = await _fileSystem.File.ReadAllLinesAsync(profilePath, cancellationToken).ConfigureAwait(false);
        var operationIds = new List<string>(lines.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var subject = $"{profilePath}:{(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (string.IsNullOrWhiteSpace(line))
            {
                errors.Add(BindingErrorCategory.Selection, subject, "generation profile cannot contain a blank line");
                continue;
            }

            if (!string.Equals(line, line.Trim(), StringComparison.Ordinal))
            {
                errors.Add(BindingErrorCategory.Selection, subject, "operation ID cannot carry leading or trailing whitespace");
                continue;
            }

            if (!seen.Add(line))
            {
                errors.Add(BindingErrorCategory.Selection, subject, $"duplicate operation ID '{line}'");
                continue;
            }

            operationIds.Add(line);
        }

        if (operationIds.Count is 0)
        {
            errors.Add(BindingErrorCategory.Selection, profilePath, "generation profile must select at least one operation");
        }

        errors.ThrowIfAny();
        return new OperationSelection
        {
            OperationIds = operationIds,
        };
    }
}
