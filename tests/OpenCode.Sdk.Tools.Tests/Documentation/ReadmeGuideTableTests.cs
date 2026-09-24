using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Documentation;

/// <summary>
/// The root README is the readme every package carries to nuget.org, and its guide table repeats
/// the guide index by hand. These tests keep the table's rows and the pages under
/// <c>docs/guide</c> one set, so a page added or removed there cannot ship without its row.
/// </summary>
public sealed class ReadmeGuideTableTests
{
    private const string DocumentationHeading = "## 📚 Documentation";
    private const string GuideIndexPage = "README.md";
    private const string GuideLinkMarker = "docs/guide/";
    private const string SectionPrefix = "## ";
    private const char TableRowPrefix = '|';

    private readonly RealFileSystem _fileSystem = new();

    [Test]
    public async Task ReadmeGuideTable_Should_List_Every_Guide_Page()
    {
        var root = new RepositoryRoot(_fileSystem).Locate();
        var linked = await ReadLinkedPagesAsync(root);

        var unlisted = GuidePages(root).Except(linked, StringComparer.Ordinal).ToList();

        await Assert.That(unlisted).IsEmpty();
    }

    [Test]
    public async Task ReadmeGuideTable_Should_Link_Only_Existing_Guide_Pages()
    {
        var root = new RepositoryRoot(_fileSystem).Locate();
        var linked = await ReadLinkedPagesAsync(root);

        var stale = linked.Except(GuidePages(root), StringComparer.Ordinal).ToList();

        await Assert.That(linked).IsNotEmpty();
        await Assert.That(stale).IsEmpty();
    }

    private IReadOnlyList<string> GuidePages(string root) =>
    [
        .. _fileSystem.Directory
            .EnumerateFiles(_fileSystem.Path.Combine(root, "docs", "guide"), "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => _fileSystem.Path.GetFileName(path))
            .Where(name => !string.Equals(name, GuideIndexPage, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal),
    ];

    private async Task<IReadOnlyList<string>> ReadLinkedPagesAsync(string root)
    {
        var lines = await _fileSystem.File.ReadAllLinesAsync(_fileSystem.Path.Combine(root, "README.md"));
        var heading = Array.FindIndex(lines, line => string.Equals(line.Trim(), DocumentationHeading, StringComparison.Ordinal));
        if (heading < 0)
        {
            throw new InvalidOperationException($"The root README has no '{DocumentationHeading}' section to read the guide table from.");
        }

        return
        [
            .. lines
                .Skip(heading + 1)
                .TakeWhile(line => !line.StartsWith(SectionPrefix, StringComparison.Ordinal))
                .Where(line => line.Length > 0 && line[0] == TableRowPrefix)
                .Select(LinkedPage)
                .OfType<string>()
                .Where(name => !string.Equals(name, GuideIndexPage, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string? LinkedPage(string row)
    {
        var start = row.IndexOf(GuideLinkMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += GuideLinkMarker.Length;
        var end = row.IndexOfAny([')', '#'], start);
        return end < 0 ? null : row[start..end];
    }
}
