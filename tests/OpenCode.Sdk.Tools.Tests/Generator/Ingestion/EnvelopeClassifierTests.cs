using System.Text.Json.Nodes;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Ingestion;

public sealed class EnvelopeClassifierTests
{
    [Test]
    public async Task Classify_Should_Recognize_Data_Envelope_Through_Named_Reference()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithRawSchema("DataEnvelope", "envelope-data.json")
            .WithOperation("v2.test.data", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("DataEnvelope"))));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Data);
    }

    [Test]
    public async Task Classify_Should_Recognize_DataLocation_Envelope_Through_Named_Reference()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithRawSchema("LocatedEnvelope", "envelope-data-location.json")
            .WithOperation("v2.test.location", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("LocatedEnvelope"))));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.DataLocation);
    }

    [Test]
    public async Task Classify_Should_Recognize_CursorData_Envelope_From_Inline_Fixture()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.test.cursor", configure: operation => operation
            .ResponseFromFixture(200, "application/json", "envelope-cursor-data.json")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.CursorData);
    }

    [Test]
    public async Task Classify_Should_Recognize_DataHasMore_Envelope_From_Inline_Fixture()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.test.more", configure: operation => operation
            .ResponseFromFixture(200, "application/json", "envelope-data-has-more.json")));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.DataHasMore);
    }

    [Test]
    public async Task Classify_Should_Return_Bare_For_Unrecognized_Json_Shape()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.test.bare", configure: operation => operation
            .Response(200, "application/json", schema => schema.Type("string"))));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
    }

    [Test]
    public async Task Classify_Should_Return_None_For_Response_Without_Content()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.test.none", configure: operation => operation.Response(204)));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.None);
    }

    [Test]
    public async Task Classify_Should_Not_Recognize_Envelope_Superset()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.test.superset", configure: operation => operation
            .Response(200, "application/json", schema => schema
                .Type("object")
                .Property("data", property => property.Type("string"))
                .Property("extra", property => property.Type("string")))));

        var result = await host.ProjectAsync(scenario);

        await Assert.That(result.Operations[0].Responses[0].EnvelopeShape).IsEqualTo(SpecEnvelopeShape.Bare);
    }

    [Test]
    public async Task Classify_Should_Refuse_Reference_Cycle()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec
            .WithSchema("First", schema => schema.Ref("Second"))
            .WithSchema("Second", schema => schema.Ref("First"))
            .WithOperation("v2.test.cycle", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("First"))));

        var ex = await host.ProjectExpectingRefusalAsync(scenario);

        await Assert.That(ex.Message).Contains("reference cycle");
        await Assert.That(ex.Message).Contains("responses/200");
    }

    [Test]
    public async Task Classify_Should_Detect_Sse_And_Preserve_Effect_Stream_Json()
    {
        var host = new OperationProjectionTestHost();
        var scenario = SpecScenario.Define(spec => spec.WithOperation("v2.session.events", configure: operation => operation
            .SseResponseFromFixture(schema => schema.Type("string"), "effect-stream.json")));

        var result = await host.ProjectAsync(scenario);

        var operation = result.Operations[0];
        var response = operation.Responses[0];
        var expected = JsonNode.Parse(new FixtureLoader().Load("effect-stream.json"));
        var actual = JsonNode.Parse(response.EffectStreamJson!);
        await Assert.That(operation.IsSse).IsTrue();
        await Assert.That(response.IsSse).IsTrue();
        await Assert.That(response.ContentType!.IsEventStream).IsTrue();
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }
}
