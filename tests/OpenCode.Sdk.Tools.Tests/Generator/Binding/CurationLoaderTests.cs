using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class CurationLoaderTests
{
    private const string CurationPath = "tools/curation.json";

    [Test]
    public async Task LoadAsync_Should_Read_Strict_Curation()
    {
        var fileSystem = CreateFileSystem("Binding.valid-curation.json");

        var curation = await new CurationLoader(fileSystem).LoadAsync(CurationPath, CancellationToken.None);

        await Assert.That(curation.Groups["health"].Placement).IsEqualTo(GroupPlacement.Root);
        await Assert.That(curation.EnvelopePayloadNames).IsEmpty();
        await Assert.That(curation.PropertyOverrides).IsEmpty();
        await Assert.That(curation.SchemaAliases).IsEmpty();
    }

    [Test]
    public async Task LoadAsync_Should_Read_Schema_Alias_Rows()
    {
        var fileSystem = CreateFileSystem("Binding.alias-curation.json");

        var curation = await new CurationLoader(fileSystem).LoadAsync(CurationPath, CancellationToken.None);

        var alias = curation.SchemaAliases.Single();
        await Assert.That(alias.Schema).IsEqualTo("InvalidRequestError1");
        await Assert.That(alias.AliasOf).IsEqualTo("InvalidRequestError");
        await Assert.That(alias.Reason).Contains("duplicate");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_Unknown_Fields()
    {
        var fileSystem = CreateFileSystem("Binding.unknown-curation-field.json");

        var exception = await Assert
            .That(async () => _ = await new CurationLoader(fileSystem).LoadAsync(CurationPath, CancellationToken.None))
            .Throws<BindingException>();

        await Assert.That(exception!.Errors.Single().Category).IsEqualTo(BindingErrorCategory.Curation);
        await Assert.That(exception.Errors.Single().Problem).Contains("mystery");
    }

    private static MockFileSystem CreateFileSystem(string fixtureName)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(CurationPath, new FixtureLoader().Load(fixtureName)));
        return fileSystem;
    }
}
