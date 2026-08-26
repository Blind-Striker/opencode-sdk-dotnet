using System.Reflection;
using System.Text.Json.Serialization;
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
        await Assert.That(curation.OperationIdentities).IsEmpty();
        await Assert.That(curation.OperationNames).IsEmpty();
        await Assert.That(curation.SchemaNames).IsEmpty();
        await Assert.That(curation.EnvelopePayloadNames).IsEmpty();
        await Assert.That(curation.SchemaAliases).IsEmpty();
    }

    [Test]
    public async Task GenerationCuration_Should_Expose_Only_Allowed_Curation_Sections()
    {
        var sections = typeof(GenerationCuration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                                       ?? throw new InvalidOperationException($"Curation property '{property.Name}' has no JSON name."))
            .Order(StringComparer.Ordinal);

        await Assert
            .That(sections)
            .IsEquivalentTo(
                ["envelopePayloadNames", "groups", "operationIdentities", "operationNames", "schemaAliases", "schemaNames"]);
    }

    [Test]
    public async Task LoadAsync_Should_Read_Reasoned_Operation_Name_Rows()
    {
        var fileSystem = CreateFileSystem("Binding.operation-name-curation.json");

        var curation = await new CurationLoader(fileSystem).LoadAsync(CurationPath, CancellationToken.None);

        var operationName = curation.OperationNames.Single();
        await Assert.That(operationName.OperationId).IsEqualTo("v2.event.subscribe");
        await Assert.That(operationName.MethodName).IsEqualTo("SubscribeAsync");
        await Assert.That(operationName.Reason).Contains("reviewed public surface");
        var schemaName = curation.SchemaNames.Single();
        await Assert.That(schemaName.Schema).IsEqualTo("V2Event");
        await Assert.That(schemaName.DotNetName).IsEqualTo("IEvent");
        await Assert.That(schemaName.Reason).Contains("transport prefix");
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

    [Test]
    [Arguments("Binding.property-overrides-curation.json", "propertyOverrides")]
    [Arguments("Binding.mutually-exclusive-queries-curation.json", "mutuallyExclusiveQueries")]
    public async Task LoadAsync_Should_Refuse_Forbidden_Semantic_Curation(string fixtureName, string section)
    {
        var fileSystem = CreateFileSystem(fixtureName);

        var exception = await Assert
            .That(async () => _ = await new CurationLoader(fileSystem).LoadAsync(CurationPath, CancellationToken.None))
            .Throws<BindingException>();

        await Assert.That(exception!.Errors.Single().Category).IsEqualTo(BindingErrorCategory.Curation);
        await Assert.That(exception.Errors.Single().Problem).Contains(section);
    }

    private static MockFileSystem CreateFileSystem(string fixtureName)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Initialize().With(new FileDescription(CurationPath, new FixtureLoader().Load(fixtureName)));
        return fileSystem;
    }
}
