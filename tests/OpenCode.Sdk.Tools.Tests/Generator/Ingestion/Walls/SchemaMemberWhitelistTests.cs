using Microsoft.OpenApi;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion.Walls;

public sealed class SchemaMemberWhitelistTests
{
    [Test]
    public async Task Check_Should_Refuse_Populated_Pinned_Member_Outside_Whitelist()
    {
        var context = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("deprecated", "true"))).Build();
        var errors = new IngestionErrorCollector();
        var loaded = await new SpecReader(context.FileSystem).LoadAsync(context.SpecPath, errors, CancellationToken.None);
        var schema = loaded.Document.Components!.Schemas!["Bad"] as OpenApiSchema
                     ?? throw new InvalidOperationException("The real reader did not produce a concrete schema.");
        var whitelist = new SchemaMemberWhitelist();

        whitelist.Check(schema, "Bad", errors);
        var ex = await Assert.That(errors.ThrowIfAny).Throws<IngestionException>();

        await Assert.That(ex!.Message).Contains("deprecated");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Check_Should_Allow_Admitted_And_Known_Ignored_Pinned_Members()
    {
        var context = SpecScenario
            .Define(spec => spec.WithSchema("Value", schema => schema
                .Type("string")
                .Description("A value.")
                .Format("custom")
                .Raw("pattern", "\"^[a-z]+$\"")))
            .Build();
        var errors = new IngestionErrorCollector();
        var loaded = await new SpecReader(context.FileSystem).LoadAsync(context.SpecPath, errors, CancellationToken.None);
        var schema = loaded.Document.Components!.Schemas!["Value"] as OpenApiSchema
                     ?? throw new InvalidOperationException("The real reader did not produce a concrete schema.");
        var whitelist = new SchemaMemberWhitelist();

        whitelist.Check(schema, "Value", errors);

        await Assert.That(errors.HasErrors).IsFalse();
    }
}
