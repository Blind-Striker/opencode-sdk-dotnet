using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

public sealed class PrefixItemsAdapterTests
{
    [Test]
    public async Task Project_Should_Produce_Tuple_From_Config_Plugin_PrefixItems()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithRawSchema("Config", "config-plugin-tuple.json"));

        var result = await host.ProjectAsync(scenario);

        var config = (ObjectNode)result.Schemas["Config"];
        var plugin = (ArrayNode)config.Properties.Single().Schema;
        var alternativesReference = (RefNode)plugin.Item;
        await Assert.That(alternativesReference.Target).IsEqualTo("Config#/properties/plugin/items");
        var alternatives = (UnionNode)result.Schemas[alternativesReference.Target];
        var tuple = (TupleNode)alternatives.Branches[1];
        await Assert.That(tuple.Items).Count().IsEqualTo(2);
        await Assert.That(tuple.Items[0]).IsTypeOf<PrimitiveNode>();
        await Assert.That(tuple.Items[1]).IsTypeOf<FreeFormObjectNode>();
        await Assert.That(tuple.Children).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Project_Should_Refuse_When_Tuple_Arity_Conflicts_With_MinItems()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithConfigPluginTuple(tuple => tuple
            .Type("array")
            .PrefixItems(item => item.Type("string"), item => item.Type("object"))
            .MinItems(1)
            .MaxItems(2)));

        var exception = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(exception.Errors.Single().Location).IsEqualTo("Config/properties/plugin/items/anyOf/1/minItems");
        await Assert.That(exception.Message).Contains("arity");
    }

    [Test]
    public async Task Project_Should_Refuse_When_Items_And_PrefixItems_Are_Combined()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithConfigPluginTuple(tuple => tuple
            .Type("array")
            .Items(item => item.Type("string"))
            .PrefixItems(item => item.Type("string"))
            .MinItems(1)
            .MaxItems(1)));

        var exception = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(exception.Message).Contains("items");
        await Assert.That(exception.Message).Contains("prefixItems");
    }

    [Test]
    public async Task Project_Should_Produce_Json_String_Node_With_Reference_Inner()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithSchema("Payload", schema => schema.Type("object").Property("id", property => property.Type("string")))
            .WithSchema("Encoded", schema => schema.Type("string").ContentSchema("application/json", inner => inner.Ref("Payload"))));

        var result = await host.ProjectAsync(scenario);

        var node = (JsonStringNode)result.Schemas["Encoded"];
        await Assert.That(node.Inner).IsTypeOf<RefNode>();
        await Assert.That(((RefNode)node.Inner).Target).IsEqualTo("Payload");
        await Assert.That(node.Children.Single()).IsSameReferenceAs(node.Inner);
    }

    [Test]
    public async Task Project_Should_Refuse_Json_String_With_Wrong_ContentMediaType()
    {
        var host = new SchemaProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithSchema("Encoded", schema => schema
            .Type("string")
            .ContentSchema("text/plain", inner => inner.Type("string"))));

        var exception = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(exception.Errors.Single().Location).IsEqualTo("Encoded/contentMediaType");
        await Assert.That(exception.Message).Contains("application/json");
    }
}
