using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Tests.Support;
using static OpenCode.Sdk.Tools.Tests.Support.BindingScenarioData;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

/// <summary>
/// The redaction floor is upstream's HTTP-recorder field list matched the way the recorder
/// matches it; curation rows add or lift a mask; a member whose name carries one of upstream's
/// secret-marker words refuses the bind until a row decides it; and every row must still mean
/// something against the bound models (ADR-0028).
/// </summary>
public sealed class SecretMemberPolicyTests
{
    private const string Reason = "The value is what the reason says it is.";

    [Test]
    [Arguments("client_secret", true)]
    [Arguments("clientSecret", true)]
    [Arguments("CLIENT-SECRET", true)]
    [Arguments("api_key", true)]
    [Arguments("apiKey", true)]
    [Arguments("password", true)]
    [Arguments("Token", true)]
    [Arguments("refresh_token", true)]
    [Arguments("x-api-key", false)]
    [Arguments("tokens", false)]
    [Arguments("apiKeyId", false)]
    [Arguments("passwordHint", false)]
    public async Task IsUpstreamRedacted_Should_Match_The_Recorder_Exactly_After_Normalizing(string wireName, bool expected)
    {
        await Assert.That(SecretMemberPolicy.IsUpstreamRedacted(wireName)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("tokens", true)]
    [Arguments("maxTokensField", true)]
    [Arguments("credentialID", true)]
    [Arguments("oauth", true)]
    [Arguments("keybind", true)]
    [Arguments("id", false)]
    [Arguments("label", false)]
    public async Task LooksSecret_Should_Find_A_Marker_Word_Anywhere_In_Any_Case(string wireName, bool expected)
    {
        await Assert.That(SecretMemberPolicy.LooksSecret(wireName)).IsEqualTo(expected);
    }

    [Test]
    public async Task Bind_Should_Mask_A_Member_On_The_Upstream_List()
    {
        var plan = await BindAsync("client_secret");

        await Assert.That(Member(plan, "client_secret").IsRedacted).IsTrue();
        await Assert.That(Member(plan, "id").IsRedacted).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Secret_Looking_Member_No_Row_Decides()
    {
        var exception = await BindRefusedAsync("apiToken");

        var error = exception.Errors.Single();
        await Assert.That(error.Subject).IsEqualTo("VaultInfo.apiToken");
        await Assert.That(error.Problem).IsEqualTo(SecretMemberPolicy.UndecidedSecretProblem);
    }

    [Test]
    public async Task Bind_Should_Print_A_Secret_Looking_Name_A_Row_Clears()
    {
        var plan = await BindAsync("apiToken", secretLookingNames: [new SecretLookingNameCuration { Property = "apiToken", Reason = Reason }]);

        await Assert.That(Member(plan, "apiToken").IsRedacted).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Mask_A_Member_A_Row_Marks()
    {
        var plan = await BindAsync("apiToken", redactedMembers: [Row("apiToken", redact: true)]);

        await Assert.That(Member(plan, "apiToken").IsRedacted).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Print_An_Upstream_Listed_Member_A_Row_Lifts()
    {
        var plan = await BindAsync("token", redactedMembers: [Row("token", redact: false)]);

        await Assert.That(Member(plan, "token").IsRedacted).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Refuse_Member_Rows_That_Decide_Nothing()
    {
        var exception = await BindRefusedAsync("client_secret", redactedMembers:
        [
            Row("absent", redact: true),
            Row("client_secret", redact: true),
            Row("id", redact: false),
            Row("id", redact: false) with { Reason = " " },
        ]);

        await Assert.That(Problems(exception)).IsEquivalentTo(
        [
            "VaultInfo.absent: redacted member curation names no generated model member",
            "VaultInfo.client_secret: redacted member curation repeats the upstream redaction list",
            "VaultInfo.id: redacted member curation lifts a mask the upstream redaction list never applies",
            "VaultInfo.id: redacted member curation is duplicated",
            "VaultInfo.id: redacted member curation must declare a reason",
            "VaultInfo.id: redacted member curation lifts a mask the upstream redaction list never applies",
        ]);
    }

    [Test]
    public async Task Bind_Should_Refuse_Name_Rows_That_Clear_Nothing()
    {
        var exception = await BindRefusedAsync("client_secret", secretLookingNames:
        [
            new SecretLookingNameCuration { Property = "absent", Reason = Reason },
            new SecretLookingNameCuration { Property = "client_secret", Reason = Reason },
            new SecretLookingNameCuration { Property = "id", Reason = Reason },
            new SecretLookingNameCuration { Property = "id", Reason = " " },
        ]);

        await Assert.That(Problems(exception)).IsEquivalentTo(
        [
            "absent: secret-looking name curation names no generated model member",
            "client_secret: secret-looking name curation clears a name the upstream redaction list masks",
            "id: secret-looking name curation names a member the secret-name wall never stops",
            "id: secret-looking name curation is duplicated",
            "id: secret-looking name curation must declare a reason",
            "id: secret-looking name curation names a member the secret-name wall never stops",
        ]);
    }

    /// <summary>A missing row is curation, never a shape wall: an operation the secret-name wall alone refuses probes as bindable.</summary>
    [Test]
    public async Task Probe_Should_Mark_An_Operation_Only_The_Secret_Name_Wall_Refuses_As_Bindable()
    {
        var document = await BindingTestHost.IngestAsync(Scenario("apiToken"));
        var probe = new PendingOperationBindabilityProbe(new BindingTestHost().Binder);

        var marks = probe.Probe(document, ["vault.get"]);

        await Assert.That(marks.Single().IsBindable).IsTrue();
    }

    private static SpecScenario Scenario(string secretMember) => SpecScenario.Define(spec => spec
        .WithSchema("VaultInfo", schema => schema
            .Type("object")
            .Property("id", property => property.Type("string"), required: true)
            .Property(secretMember, property => property.Type("string"), required: false))
        .WithOperation("vault.get", path: "/api/vault", configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("VaultInfo"))));

    private static async Task<EmitPlan> BindAsync(string secretMember,
        IReadOnlyList<RedactedMemberCuration>? redactedMembers = null,
        IReadOnlyList<SecretLookingNameCuration>? secretLookingNames = null)
    {
        var document = await BindingTestHost.IngestAsync(Scenario(secretMember));
        return new BindingTestHost().Bind(document, Selection("vault.get"),
            Curation(Groups("vault", RootGroup()), redactedMembers: redactedMembers, secretLookingNames: secretLookingNames));
    }

    private static async Task<BindingException> BindRefusedAsync(string secretMember,
        IReadOnlyList<RedactedMemberCuration>? redactedMembers = null,
        IReadOnlyList<SecretLookingNameCuration>? secretLookingNames = null)
    {
        var document = await BindingTestHost.IngestAsync(Scenario(secretMember));
        var curation = Curation(Groups("vault", RootGroup()), redactedMembers: redactedMembers, secretLookingNames: secretLookingNames);
        return Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(document, Selection("vault.get"), curation));
    }

    private static RedactedMemberCuration Row(string property, bool redact) =>
        new() { Model = "VaultInfo", Property = property, Redact = redact, Reason = Reason };

    private static ModelPropertyPlan Member(EmitPlan plan, string wireName) =>
        plan.Models.OfType<ObjectModelPlan>().Single(static model => model.Name == "VaultInfo")
            .Properties.Single(property => property.WireName == wireName);

    private static string[] Problems(BindingException exception) =>
        [.. exception.Errors.Select(static error => $"{error.Subject}: {error.Problem}")];
}
