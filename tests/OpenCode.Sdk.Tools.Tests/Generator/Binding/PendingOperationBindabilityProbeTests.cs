using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Abstractions;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class PendingOperationBindabilityProbeTests
{
    private static readonly SpecScenario WidgetScenario = SpecScenario.Define(spec => spec
        .WithSchema("WidgetInfo", schema => schema
            .Type("object")
            .Property("id", property => property.Type("string"), required: true))
        .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))
        .WithOperation("v2.widget.connect", path: "/api/widget/connect", configure: operation => operation
            .Extension("x-websocket", "true"))
        .WithOperation("v2.widget.tail", path: "/api/widget/*", configure: operation => operation
            .Extension("x-websocket", "true")));

    [Test]
    public async Task Probe_Should_Mark_A_Standalone_Operation_As_Bindable()
    {
        var document = await BindingTestHost.IngestAsync(WidgetScenario);
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, ["v2.widget.list"]);

        await Assert.That(marks.Count).IsEqualTo(1);
        await Assert.That(marks[0].OperationId).IsEqualTo("v2.widget.list");
        await Assert.That(marks[0].IsBindable).IsTrue();
        await Assert.That(marks[0].RefusalMessage).IsNull();
    }

    [Test]
    public async Task Probe_Should_Mark_A_Walled_Operation_Refused_With_The_Walls_Verbatim_Message()
    {
        var document = await BindingTestHost.IngestAsync(WidgetScenario);
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, ["v2.widget.connect"]);

        await Assert.That(marks.Count).IsEqualTo(1);
        await Assert.That(marks[0].OperationId).IsEqualTo("v2.widget.connect");
        await Assert.That(marks[0].IsBindable).IsFalse();
        await Assert.That(marks[0].RefusalMessage).IsEqualTo("WebSocket operations are not supported in M1");
    }

    [Test]
    public async Task Probe_Should_List_Every_Independent_Wall_In_Binder_Order_When_An_Operation_Fails_Several_Walls()
    {
        var document = await BindingTestHost.IngestAsync(WidgetScenario);
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, ["v2.widget.tail"]);

        await Assert.That(marks.Count).IsEqualTo(1);
        await Assert.That(marks[0].IsBindable).IsFalse();
        await Assert.That(marks[0].RefusalMessage).IsEqualTo(
            "wildcard paths are not supported in M1; WebSocket operations are not supported in M1");
    }

    [Test]
    public async Task Probe_Should_Collapse_A_Problem_Repeated_Across_Independent_Subjects_To_One_Occurrence()
    {
        var (document, _, _) = await BindingTestHost.LoadPinnedInputsAsync();
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, ["v2.config.get"]);

        await Assert.That(marks.Count).IsEqualTo(1);
        await Assert.That(marks[0].IsBindable).IsFalse();
        var walls = marks[0].RefusalMessage!.Split("; ");
        await Assert.That(walls).Contains("inline nominal schema was not promoted into the graph");
        await Assert.That(walls.Count(static wall => wall is "inline nominal schema was not promoted into the graph"))
            .IsEqualTo(1);
    }

    [Test]
    public async Task Probe_Should_Refuse_Without_Crashing_When_The_Binder_Throws_Unexpectedly()
    {
        var document = await BindingTestHost.IngestAsync(WidgetScenario);
        var probe = new PendingOperationBindabilityProbe(new ThrowingSpecBinder());

        var marks = probe.Probe(document, ["v2.widget.list"]);

        await Assert.That(marks.Count).IsEqualTo(1);
        await Assert.That(marks[0].OperationId).IsEqualTo("v2.widget.list");
        await Assert.That(marks[0].IsBindable).IsFalse();
        await Assert.That(marks[0].RefusalMessage).IsNotNull();
        await Assert.That(marks[0].RefusalMessage).Contains(nameof(InvalidOperationException));
        await Assert.That(marks[0].RefusalMessage).Contains("boom");
    }

    [Test]
    public async Task Probe_Should_Preserve_The_Requested_Operation_Order()
    {
        var document = await BindingTestHost.IngestAsync(WidgetScenario);
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, ["v2.widget.connect", "v2.widget.list"]);

        await Assert.That(marks.Select(static mark => mark.OperationId)
                .SequenceEqual(["v2.widget.connect", "v2.widget.list"], StringComparer.Ordinal))
            .IsTrue();
    }

    [Test]
    [Arguments("v2.credential.activate")]
    [Arguments("v2.form.request.list")]
    [Arguments("v2.integration.connect.key")]
    [Arguments("v2.integration.list")]
    [Arguments("v2.integration.oauth.connect")]
    [Arguments("v2.project.update")]
    [Arguments("v2.session.environment")]
    [Arguments("v2.session.form.create")]
    [Arguments("v2.session.form.get")]
    [Arguments("v2.session.form.reply")]
    [Arguments("v2.session.form.state")]
    [Arguments("v2.session.messageUpdate")]
    [Arguments("v2.session.view")]
    [Arguments("v2.workspace.destroy")]
    public async Task Probe_Should_Mark_Every_Known_Wall_Free_Pending_Operation_As_Bindable(string operationId)
    {
        var (document, _, _) = await BindingTestHost.LoadPinnedInputsAsync();
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, [operationId]);

        await Assert.That(marks.Count).IsEqualTo(1);
        await Assert.That(marks[0].IsBindable).IsTrue();
        await Assert.That(marks[0].RefusalMessage).IsNull();
    }

    private sealed class ThrowingSpecBinder : ISpecBinder
    {
        public EmitPlan Bind(SpecDocument document, OperationSelection selection, GenerationCuration curation) =>
            throw new InvalidOperationException("boom");
    }
}
