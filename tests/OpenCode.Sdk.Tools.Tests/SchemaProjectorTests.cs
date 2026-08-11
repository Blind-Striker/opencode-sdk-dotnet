using System.Text.Json.Nodes;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Projection;
using OpenCode.Sdk.Tools.Generator.Ingestion.Walls;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class SchemaProjectorTests
{
    [Test]
    public async Task Project_Should_Produce_String_Primitive_Node()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema.Type("string")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"]).IsTypeOf<PrimitiveNode>();
        await Assert.That(((PrimitiveNode)result.Schemas["Value"]).Kind).IsEqualTo(PrimitiveKind.String);
    }

    [Test]
    public async Task Project_Should_Produce_Number_Primitive_Node()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema.Type("number")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"]).IsTypeOf<PrimitiveNode>();
        await Assert.That(((PrimitiveNode)result.Schemas["Value"]).Kind).IsEqualTo(PrimitiveKind.Number);
    }

    [Test]
    public async Task Project_Should_Produce_Integer_Primitive_Node()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema.Type("integer")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"]).IsTypeOf<PrimitiveNode>();
        await Assert.That(((PrimitiveNode)result.Schemas["Value"]).Kind).IsEqualTo(PrimitiveKind.Integer);
    }

    [Test]
    public async Task Project_Should_Produce_Boolean_Primitive_Node()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema.Type("boolean")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"]).IsTypeOf<PrimitiveNode>();
        await Assert.That(((PrimitiveNode)result.Schemas["Value"]).Kind).IsEqualTo(PrimitiveKind.Boolean);
    }

    [Test]
    public async Task Project_Should_Record_Format()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema.Type("string").Format("uuid")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"].Format).IsEqualTo("uuid");
    }

    [Test]
    public async Task Project_Should_Record_Description()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Value", schema => schema.Type("string").Description("A value.")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Value"].Description).IsEqualTo("A value.");
    }

    [Test]
    public async Task Project_Should_Produce_Unrestricted_Node_For_Description_Only_Schema()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("ToolResult", schema => schema.Description("Any result.")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["ToolResult"]).IsTypeOf<UnrestrictedNode>();
        await Assert.That(result.Schemas["ToolResult"].Description).IsEqualTo("Any result.");
    }

    [Test]
    public async Task Project_Should_Produce_Unrestricted_Node_For_Empty_Schema()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("ToolResult", schema => schema.Unrestricted()));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["ToolResult"]).IsTypeOf<UnrestrictedNode>();
    }

    [Test]
    public async Task Project_Should_Keep_Dotted_Schema_Name_Verbatim()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("session.info", schema => schema.Type("string")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas.ContainsKey("session.info")).IsTrue();
    }

    [Test]
    public async Task Project_Should_Produce_Enum_Node_For_Multiple_String_Values()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Mode", schema => schema.Type("string").Enum("fast", "safe")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Mode"]).IsTypeOf<EnumNode>();
        var node = (EnumNode)result.Schemas["Mode"];
        await Assert.That(node.Values).Count().IsEqualTo(2);
        await Assert.That(node.Values[0]).IsEqualTo("fast");
        await Assert.That(node.Values[1]).IsEqualTo("safe");
    }

    [Test]
    public async Task Project_Should_Produce_Array_Node_With_Item()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Values", schema => schema.Type("array").Items(item => item.Type("integer"))));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Values"]).IsTypeOf<ArrayNode>();
        var item = ((ArrayNode)result.Schemas["Values"]).Item;
        await Assert.That(item).IsTypeOf<PrimitiveNode>();
        await Assert.That(((PrimitiveNode)item).Kind).IsEqualTo(PrimitiveKind.Integer);
    }

    [Test]
    public async Task Project_Should_Promote_Inline_Enum_Under_Items()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec =>
            spec.WithSchema("Modes", schema => schema.Type("array").Items(item => item.Type("string").Enum("fast", "safe"))));

        var result = await host.ProjectAsync(scenario);

        var item = ((ArrayNode)result.Schemas["Modes"]).Item;
        await Assert.That(item).IsTypeOf<RefNode>();
        await Assert.That(((RefNode)item).Target).IsEqualTo("Modes#/items");
        await Assert.That(result.Schemas["Modes#/items"]).IsTypeOf<EnumNode>();
    }

    [Test]
    public async Task Project_Should_Produce_Ref_Node_For_Existing_Target()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithSchema("Target", schema => schema.Type("string"))
            .WithSchema("Alias", schema => schema.Ref("Target")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Schemas["Alias"]).IsTypeOf<RefNode>();
        await Assert.That(((RefNode)result.Schemas["Alias"]).Target).IsEqualTo("Target");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Ref_Target_Is_Missing()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Ref("Missing")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("Bad");
        await Assert.That(ex.Message).Contains("Missing");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Array_Has_No_Items()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Bad", schema => schema.Type("array")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("items");
        await Assert.That(ex.Message).Contains("Bad");
    }

    [Test]
    public async Task Project_Should_Batch_Errors_From_Multiple_Schemas()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithSchema("BadArray", schema => schema.Type("array"))
            .WithSchema("BadTitle", schema => schema.Raw("title", "\"unsupported\"")));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Errors).Count().IsEqualTo(2);
        await Assert.That(ex.Message).Contains("BadArray");
        await Assert.That(ex.Message).Contains("BadTitle");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Promoted_Graph_Key_Collides()
    {
        var scenario = SpecScenario.Define(spec => spec
            .WithSchema("First", schema => schema.Type("string").Enum("fast", "safe"))
            .WithSchema("Second", schema => schema.Type("string").Enum("on", "off")));
        var context = scenario.Build();
        var errors = new IngestionErrorCollector();
        var loaded = await new SpecReader(context.FileSystem).LoadAsync(context.SpecPath, errors, CancellationToken.None);
        var keys = new GraphKeyBuilder();
        var projector = new SchemaProjector(new SchemaWallPolicy(), keys);
        var state = new ProjectionState(errors, new Dictionary<string, JsonNode>(StringComparer.Ordinal));

        _ = projector.Project(loaded.Document.Components!.Schemas!["First"], "Shared", "/value", state);
        _ = projector.Project(loaded.Document.Components.Schemas["Second"], "Shared", "/value", state);
        var ex = await Assert.That(errors.ThrowIfAny).Throws<IngestionException>();

        await Assert.That(ex!.Message).Contains("collision");
        await Assert.That(ex.Message).Contains("Shared#/value");
    }
}
