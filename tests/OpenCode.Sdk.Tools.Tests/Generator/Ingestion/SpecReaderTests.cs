using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

public sealed class SpecReaderTests
{
    [Test]
    public async Task LoadAsync_Should_Return_Document_And_Raw_For_Valid_31_Document()
    {
        var context = SpecScenario.Define(_ => { }).Build();
        var reader = new SpecReader(context.FileSystem);

        var loaded = await reader.LoadAsync(context.SpecPath, new IngestionErrorCollector(), CancellationToken.None);

        await Assert.That(loaded.Document.Paths).IsNotNull();
        await Assert.That(loaded.Raw["openapi"]!.GetValue<string>()).IsEqualTo("3.1.0");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_When_Version_Is_Not_31()
    {
        var ex = await LoadExpectingRefusalAsync(SpecScenario.Define(spec => spec.WithOpenApiVersion("3.2.0")));

        await Assert.That(ex.Message).Contains("3.2");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_When_Spec_File_Is_Missing()
    {
        var reader = new SpecReader(new MockFileSystem());
        var ex = await Assert
            .That(async () => _ = await reader.LoadAsync("spec/openapi.json", new IngestionErrorCollector(), CancellationToken.None))
            .Throws<IngestionException>();
        await Assert.That(ex!.Message).Contains("spec/openapi.json");
    }

    [Test]
    public async Task LoadAsync_Should_Translate_Reader_Crash_When_Schema_Is_Boolean()
    {
        var specScenario = SpecScenario.Define(spec => spec.WithRawSchema("Bad", "boolean-property-schema.json"));

        var ex = await LoadExpectingRefusalAsync(specScenario);
        await Assert.That(ex.Message).Contains("reader failed");
    }

    [Test]
    public async Task LoadAsync_Should_Promote_Reader_Diagnostics_To_Errors()
    {
        var specScenario = SpecScenario
            .Define(spec => spec.WithOperation("v2.test.get", configure: operation => operation.Raw("madeUpKey", "{}")));

        var ex = await LoadExpectingRefusalAsync(specScenario);
        await Assert.That(ex.Message).Contains("madeUpKey");
    }

    [Test]
    public async Task LoadAsync_Should_Refuse_When_Json_Is_Malformed()
    {
        var specScenario = SpecScenario.FromRawJson("{ not json");

        var ex = await LoadExpectingRefusalAsync(specScenario);
        await Assert.That(ex.Message).Contains("reader failed");
    }

    private static async Task<IngestionException> LoadExpectingRefusalAsync(SpecScenario scenario)
    {
        var context = scenario.Build();
        var reader = new SpecReader(context.FileSystem);
        var errors = new IngestionErrorCollector();
        var ex = await Assert
            .That(async () => _ = await reader.LoadAsync(context.SpecPath, errors, CancellationToken.None))
            .Throws<IngestionException>();
        return ex!;
    }
}
