using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion.Walls;

public sealed class SchemaWallPolicyTests
{
    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_AllOf()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.AllOf(branch => branch.Type("string"))));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("allOf");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Type_Is_An_Array()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("type", "[\"string\",\"null\"]")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("type");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_Discriminator()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("discriminator", "{}")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("discriminator");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_If_And_Then()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("if", "{}").Raw("then", "{}")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("if");
        await Assert.That(ex.Message).Contains("then");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_Title()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("title", "\"unsupported\"")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("title");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_Default()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("default", "\"unsupported\"")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("default");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Is_ReadOnly()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("readOnly", "true")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("readOnly");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_MultipleOf()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("multipleOf", "2")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("multipleOf");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Uses_UniqueItems()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("uniqueItems", "true")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("uniqueItems");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Has_Unknown_Raw_Keyword()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("madeUpKeyword", "true")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("madeUpKeyword");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Schema_Has_Extension()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Raw("x-schema", "true")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("x-schema");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Ignore_Known_Validation_Keywords()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema
            .Type("string")
            .Raw("pattern", "\"^[a-z]+$\"")
            .Raw("minimum", "0")
            .Raw("maximum", "10")
            .Raw("exclusiveMinimum", "1")
            .Raw("minItems", "1")
            .Raw("maxItems", "5")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"]).IsTypeOf<PrimitiveNode>();
    }
}
