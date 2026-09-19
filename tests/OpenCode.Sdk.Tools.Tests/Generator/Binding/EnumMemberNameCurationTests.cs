using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Tests.Support;
using static OpenCode.Sdk.Tools.Tests.Support.BindingScenarioData;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

/// <summary>
/// The <c>enumMemberNames</c> curation channel: a reason-bearing row names the C# member one
/// enum value becomes, where the mechanical casing would produce a digit-led <c>Value…</c> name;
/// every other value keeps its mechanical name, and a row that names nothing the document has
/// refuses like every other channel.
/// </summary>
public sealed class EnumMemberNameCurationTests
{
    [Test]
    public async Task Bind_Should_Name_A_Digit_Led_Value_Mechanically_Without_A_Row()
    {
        var document = await BindingTestHost.IngestAsync(Scenario());

        var plan = new BindingTestHost().Bind(document, Selection("mcp.get"), Curation(Groups("mcp", RootGroup())));

        var protocol = plan.Models.OfType<EnumModelPlan>().Single(static model => model.Name == "McpProtocol");
        await Assert.That(protocol.Values.Single(static value => value.WireValue == "2026-07-28").Name).IsEqualTo("Value20260728");
        await Assert.That(protocol.Values.Single(static value => value.WireValue == "legacy").Name).IsEqualTo("Legacy");
    }

    [Test]
    public async Task Bind_Should_Apply_A_Reasoned_Member_Name_To_Its_Value_Only()
    {
        var document = await BindingTestHost.IngestAsync(Scenario());
        var curation = Curation(Groups("mcp", RootGroup()),
            enumMemberNames: [EnumMemberName("Mcp.Protocol", "2026-07-28", "Revision20260728")]);

        var plan = new BindingTestHost().Bind(document, Selection("mcp.get"), curation);

        var protocol = plan.Models.OfType<EnumModelPlan>().Single(static model => model.Name == "McpProtocol");
        await Assert.That(protocol.Values.Single(static value => value.WireValue == "2026-07-28").Name).IsEqualTo("Revision20260728");
        await Assert.That(protocol.Values.Single(static value => value.WireValue == "legacy").Name).IsEqualTo("Legacy");
        await Assert.That(protocol.Values.Single(static value => value.WireValue == "auto").Name).IsEqualTo("Auto");
    }

    [Test]
    [Arguments("Mcp.Missing", "2026-07-28", "Revision20260728", "curated schema does not exist in the spec")]
    [Arguments("Holder", "2026-07-28", "Revision20260728", "is not an enum")]
    [Arguments("Mcp.Protocol", "2099-01-01", "Revision20990101", "does not carry the value")]
    [Arguments("Mcp.Protocol", "2026-07-28", "2026", "is not a valid C# identifier")]
    [Arguments("Mcp.Protocol", "2026-07-28", "Legacy", "collides")]
    public async Task Bind_Should_Refuse_A_Row_That_Names_Nothing_The_Document_Has(
        string schema, string value, string dotnetName, string problem)
    {
        var document = await BindingTestHost.IngestAsync(Scenario());
        var curation = Curation(Groups("mcp", RootGroup()),
            enumMemberNames: [EnumMemberName(schema, value, dotnetName)]);

        var exception = Assert.Throws<BindingException>(() => new BindingTestHost().Bind(document, Selection("mcp.get"), curation));

        await Assert.That(exception.Errors.Any(error => error.Problem.Contains(problem, StringComparison.Ordinal)))
            .IsTrue()
            .Because(string.Join(" | ", exception.Errors.Select(static error => error.Subject + ": " + error.Problem)));
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Row_Without_A_Reason()
    {
        var document = await BindingTestHost.IngestAsync(Scenario());
        var curation = Curation(Groups("mcp", RootGroup()),
            enumMemberNames: [EnumMemberName("Mcp.Protocol", "2026-07-28", "Revision20260728", reason: " ")]);

        var exception = Assert.Throws<BindingException>(() => new BindingTestHost().Bind(document, Selection("mcp.get"), curation));

        await Assert.That(exception.Errors.Any(static error => error.Problem.Contains("must declare a reason", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Duplicated_Row()
    {
        var document = await BindingTestHost.IngestAsync(Scenario());
        var curation = Curation(Groups("mcp", RootGroup()),
            enumMemberNames:
            [
                EnumMemberName("Mcp.Protocol", "2026-07-28", "Revision20260728"),
                EnumMemberName("Mcp.Protocol", "2026-07-28", "Revision2026"),
            ]);

        var exception = Assert.Throws<BindingException>(() => new BindingTestHost().Bind(document, Selection("mcp.get"), curation));

        await Assert.That(exception.Errors.Any(static error => error.Problem.Contains("is duplicated", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>The pinned shape in miniature: an enum whose first value leads with a digit, reached through one response.</summary>
    private static SpecScenario Scenario() =>
        SpecScenario.Define(static spec => spec
            .WithSchema("Mcp.Protocol", static schema => schema.Type("string").Enum("2026-07-28", "legacy", "auto"))
            .WithSchema("Holder", static schema => schema.Type("object")
                .Property("protocol", static property => property.Ref("Mcp.Protocol"), required: true))
            .WithOperation("mcp.get", path: "/api/mcp", configure: static operation => operation
                .Response(200, "application/json", static schema => schema.Ref("Holder"))));
}
