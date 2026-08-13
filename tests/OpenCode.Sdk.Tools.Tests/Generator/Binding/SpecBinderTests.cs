using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class SpecBinderTests
{
    private static readonly string[] ExpectedErrorTypeNames =
    [
        "InvalidRequestError",
        "MessageNotFoundError",
        "SessionNotFoundError",
        "UnauthorizedError",
    ];

    [Test]
    public async Task Bind_Should_Create_The_Selected_Pinned_Emit_Plan()
    {
        var (document, selection, curation) = await LoadPinnedInputsAsync();

        var plan = new BindingTestHost().Bind(document, selection, curation);

        await Assert.That(plan.SelectedOperationIds.SequenceEqual(selection.OperationIds, StringComparer.Ordinal)).IsTrue();
        await Assert.That(plan.PendingOperations).IsNotEmpty();
        await Assert.That(plan.Models.Any(static model => model.Name == "PromptFileSourceUri")).IsTrue();
        await Assert.That(plan.Models.Any(static model => model.Name == "ConfigInfo")).IsFalse();

        var promptFile = plan.Models.OfType<ObjectModelPlan>().Single(static model => model.Name == "PromptFileSourceUri");
        var promptUri = promptFile.Properties.Single(static property => property.WireName == "uri").Type;
        await Assert.That(promptUri).IsTypeOf<NamedTypeReferencePlan>();
        await Assert.That(((NamedTypeReferencePlan)promptUri).Name).IsEqualTo("Uri");

        var toolFile = plan.Models.OfType<ObjectModelPlan>().Single(static model => model.Name == "ToolFileContent");
        var toolUri = toolFile.Properties.Single(static property => property.WireName == "uri").Type;
        await Assert.That(toolUri).IsTypeOf<NamedTypeReferencePlan>();
        await Assert.That(((NamedTypeReferencePlan)toolUri).Name).IsEqualTo("Uri");

        var sessionMessage = plan.Unions.Single(static union => union.Name == "SessionMessageInfo");
        await Assert.That(sessionMessage.MarkerWireName).IsEqualTo("type");
        await Assert.That(sessionMessage.Variants).Count().IsEqualTo(10);

        var compaction = plan.Unions.Single(static union => union.Name == "SessionMessageCompaction");
        await Assert.That(compaction.MarkerWireName).IsEqualTo("status");
        await Assert.That(compaction.BaseTypeName).IsEqualTo("SessionMessageInfo");
        await Assert.That(compaction.FixedMarker!.WireName).IsEqualTo("type");
        await Assert.That(compaction.FixedMarker.Value).IsEqualTo("compaction");
        await Assert.That(compaction.Variants).Count().IsEqualTo(3);

        await Assert.That(plan.Unions.Any(static union => union.Name == "SessionMessageAssistantContent")).IsTrue();
        await Assert.That(plan.Unions.Any(static union => union.Name == "SessionMessageToolState")).IsTrue();
        await Assert.That(plan.Unions.Any(static union => union.Name == "PromptFileSource")).IsTrue();
        await Assert.That(plan.Unions.Any(static union => union.Name == "ToolContent")).IsTrue();

        var errors = plan.Unions.Single(static union => union.Name == "OpenCodeError");
        await Assert.That(errors.Variants.Select(static variant => variant.TypeName).Order(StringComparer.Ordinal)
            .SequenceEqual(ExpectedErrorTypeNames, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Be_Deterministic_For_The_Selected_Pin()
    {
        var (document, selection, curation) = await LoadPinnedInputsAsync();
        var host = new BindingTestHost();

        var first = host.Bind(document, selection, curation);
        var second = host.Bind(document, selection, curation);

        await Assert.That(first.Models.Select(static model => model.Name)
            .SequenceEqual(second.Models.Select(static model => model.Name), StringComparer.Ordinal)).IsTrue();
        await Assert.That(first.Unions.Select(static union => union.Name)
            .SequenceEqual(second.Unions.Select(static union => union.Name), StringComparer.Ordinal)).IsTrue();
        await Assert.That(first.Registry.TypeNames.SequenceEqual(second.Registry.TypeNames, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Report_Selection_And_Curation_Errors_Together()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.health.get")));
        var selection = Selection("v2.health.get", "v2.missing.get");
        var curation = Curation(
            new Dictionary<string, GroupCuration>(StringComparer.Ordinal)
            {
                ["health"] = RootGroup(),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["v2.orphan.get"] = "Orphan",
            });

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(document, selection, curation));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Selection)).IsTrue();
        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Curation)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_Structural_Union_In_Selected_Closure()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("Choice", schema => schema.AnyOf(
                branch => branch.Type("string"),
                branch => branch.Type("boolean")))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("Choice")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), })));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Schema).Problem)
            .Contains("structural union");
    }

    [Test]
    public async Task Bind_Should_Require_Selected_Envelope_Payload_Name()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.session.message", path: "/api/session/{sessionID}/message", configure: operation => operation
                .Parameter("sessionID", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("data", property => property.Type("string"), required: true)))));
        var groups = new Dictionary<string, GroupCuration>(StringComparer.Ordinal)
        {
            ["session"] = ClientGroup(),
        };

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.session.message"),
            Curation(groups)));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Curation).Problem)
            .Contains("payload name");
    }

    [Test]
    public async Task Bind_Should_Refuse_Operation_Curation_For_Pending_Operation()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.health.get")
            .WithOperation("v2.session.message", path: "/api/session/message", configure: operation => operation
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("data", property => property.Type("string"), required: true)))));
        var envelopeNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v2.session.message"] = "Message",
        };

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), }, envelopeNames)));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Curation).Problem)
            .Contains("not selected");
    }

    [Test]
    public async Task Bind_Should_Collapse_Duplicate_References_Into_Semantic_Type_Plans()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("Shared", schema => schema.Type("object")
                .Property("value", property => property.Type("string"), required: true))
            .WithSchema("Container", schema => schema.Type("object")
                .Property("first", property => property.Ref("Shared"), required: true)
                .Property("items", property => property.Type("array").Items(item => item.Ref("Shared")), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("Container")))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), }));

        await Assert.That(plan.Models.Count(static model => model.Name == "Shared")).IsEqualTo(1);
        var container = plan.Models.OfType<ObjectModelPlan>().Single(static model => model.Name == "Container");
        var items = container.Properties.Single(static property => property.WireName == "items").Type;
        await Assert.That(items).IsTypeOf<ListTypeReferencePlan>();
        await Assert.That(((ListTypeReferencePlan)items).ElementType).IsTypeOf<NamedTypeReferencePlan>();
        await Assert.That(((NamedTypeReferencePlan)((ListTypeReferencePlan)items).ElementType).Name).IsEqualTo("Shared");
    }

    [Test]
    public async Task Bind_Should_Bind_Nested_Marked_Union_With_The_Discriminating_Marker()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("Alpha", schema => schema.Type("object")
                .Property("type", property => property.Type("string").Enum("alpha"), required: true))
            .WithSchema("WrapOne", schema => schema.Type("object")
                .Property("type", property => property.Type("string").Enum("wrap"), required: true)
                .Property("status", property => property.Type("string").Enum("one"), required: true))
            .WithSchema("WrapTwo", schema => schema.Type("object")
                .Property("type", property => property.Type("string").Enum("wrap"), required: true)
                .Property("status", property => property.Type("string").Enum("two"), required: true))
            .WithSchema("Wrap", schema => schema.AnyOf(one => one.Ref("WrapOne"), two => two.Ref("WrapTwo")))
            .WithSchema("Outer", schema => schema.AnyOf(alpha => alpha.Ref("Alpha"), wrap => wrap.Ref("Wrap")))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("Outer")))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), }));

        var outer = plan.Unions.Single(static union => union.Name == "Outer");
        await Assert.That(outer.MarkerWireName).IsEqualTo("type");
        await Assert.That(outer.Variants.Select(static variant => variant.Tag).Order(StringComparer.Ordinal)
            .SequenceEqual(["alpha", "wrap"], StringComparer.Ordinal)).IsTrue();

        var nested = plan.Unions.Single(static union => union.Name == "Wrap");
        await Assert.That(nested.MarkerWireName).IsEqualTo("status");
        await Assert.That(nested.BaseTypeName).IsEqualTo("Outer");
        await Assert.That(nested.FixedMarker!.WireName).IsEqualTo("type");
        await Assert.That(nested.FixedMarker.Value).IsEqualTo("wrap");
    }

    [Test]
    public async Task Bind_Should_Refuse_Union_When_Branches_Share_No_Discriminating_Marker()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("First", schema => schema.Type("object")
                .Property("type", property => property.Type("string").Enum("same"), required: true))
            .WithSchema("Second", schema => schema.Type("object")
                .Property("type", property => property.Type("string").Enum("same"), required: true))
            .WithSchema("Twin", schema => schema.AnyOf(first => first.Ref("First"), second => second.Ref("Second")))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("Twin")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), })));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Schema).Problem)
            .Contains("no discriminating marker");
    }

    [Test]
    public async Task Bind_Should_Require_Curation_For_Every_Selected_Group()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec.WithOperation("v2.health.get")));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal))));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Curation).Problem)
            .Contains("no curation row");
    }

    [Test]
    public async Task Bind_Should_Report_Duplicate_Property_Overrides_As_Binding_Errors()
    {
        var document = await IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("Resource", schema => schema.Type("object")
                .Property("uri", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("Resource")))));
        var curation = Curation(
            new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), },
            propertyOverrides: [UriOverride("Resource", "uri"), UriOverride("Resource", "uri")]);

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            curation));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Curation).Problem)
            .Contains("duplicated");
    }

    private static async Task<(SpecDocument Document, OperationSelection Selection, GenerationCuration Curation)> LoadPinnedInputsAsync()
    {
        var fileSystem = new RealFileSystem();
        var fixtureRoot = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var document = await new SpecIngestion(fileSystem)
            .IngestAsync(fileSystem.Path.Combine(fixtureRoot, "openapi.json"), CancellationToken.None);
        var selection = await new OperationSelectionLoader(fileSystem)
            .LoadAsync(fileSystem.Path.Combine(fixtureRoot, "generation-profile.txt"), CancellationToken.None);
        var curation = await new CurationLoader(fileSystem)
            .LoadAsync(fileSystem.Path.Combine(fixtureRoot, "curation.json"), CancellationToken.None);
        return (document, selection, curation);
    }

    private static async Task<SpecDocument> IngestAsync(SpecScenario scenario)
    {
        var context = scenario.Build();
        return await new SpecIngestion(context.FileSystem).IngestAsync(context.SpecPath, CancellationToken.None);
    }

    private static OperationSelection Selection(params string[] operationIds) =>
        new()
        {
            OperationIds = operationIds,
        };

    private static GenerationCuration Curation(
        IReadOnlyDictionary<string, GroupCuration> groups,
        IReadOnlyDictionary<string, string>? envelopePayloadNames = null,
        IReadOnlyList<PropertyOverride>? propertyOverrides = null) =>
        new()
        {
            Groups = groups,
            EnvelopePayloadNames = envelopePayloadNames ?? new Dictionary<string, string>(StringComparer.Ordinal),
            PropertyOverrides = propertyOverrides ?? [],
        };

    private static PropertyOverride UriOverride(string schema, string property) =>
        new()
        {
            Schema = schema,
            Property = property,
            Type = PropertyOverrideType.Uri,
            Reason = "The fixture omits format: uri.",
        };

    private static GroupCuration RootGroup() =>
        new()
        {
            Placement = GroupPlacement.Root,
        };

    private static GroupCuration ClientGroup() =>
        new()
        {
            Placement = GroupPlacement.Client,
            ClientName = "Sessions",
            HandleName = "SessionClient",
        };
}
