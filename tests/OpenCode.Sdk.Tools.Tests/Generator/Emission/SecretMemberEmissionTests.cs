using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

/// <summary>
/// A model with a secret member prints itself (ADR-0028). The override must keep the compiler's
/// own shape exactly — the same members in the same order, the same spacing, an absent value
/// empty — so the only difference from the synthesized ToString is the masked value.
/// </summary>
public sealed class SecretMemberEmissionTests
{
    [Test]
    public async Task Emit_Should_Override_ToString_Only_On_A_Model_With_A_Secret_Member()
    {
        var sources = ModelEmitter.Emit(Redact(EmitterPlanFixture.CreateModelSnapshot(), "ExampleItem", "note"));

        await Assert.That(EmitterSnapshot.Content(sources, "Models/ExampleItem.cs"))
            .Contains("public override string ToString() => RecordPrinter.Format(nameof(ExampleItem), (\"ID\", ID), (\"Note\", RecordPrinter.Redact(Note))");
        await Assert.That(EmitterSnapshot.Content(sources, "Models/OpenSettings.cs")).DoesNotContain("ToString");
    }

    [Test]
    [Arguments("ExampleItem", "note", """{"id":"i","note":"hunter2","peak":1,"requiredNullable":null,"requiredTags":["a"]}""", "Note = hunter2")]
    [Arguments("ExampleItem", "note", """{"id":"i","peak":1,"requiredNullable":null,"requiredTags":[]}""", null)]
    [Arguments("OpenSettings", "timeout", """{"timeout":5,"extra":1}""", "Timeout = 5")]
    public async Task Emit_Should_Print_The_Compilers_Shape_With_Only_The_Secret_Masked(string model, string member, string payload, string? printedSecret)
    {
        var plain = await PrintAsync(EmitterPlanFixture.Create(), model, payload);
        var masked = await PrintAsync(Redact(EmitterPlanFixture.Create(), model, member), model, payload);

        var expected = printedSecret is null
            ? plain
            : plain.Replace(printedSecret, printedSecret[..printedSecret.IndexOf('=', StringComparison.Ordinal)] + "= [REDACTED]", StringComparison.Ordinal);
        await Assert.That(masked).IsEqualTo(expected);
        if (printedSecret is not null)
        {
            await Assert.That(plain).Contains(printedSecret);
        }
    }

    private static EmitPlan Redact(EmitPlan plan, string modelName, string wireName) =>
        plan with
        {
            Models =
            [
                .. plan.Models.Select(model => model is ObjectModelPlan objectModel && objectModel.Name == modelName
                    ? objectModel with
                    {
                        Properties =
                        [
                            .. objectModel.Properties.Select(property => property.WireName == wireName ? property with { IsRedacted = true } : property),
                        ],
                    }
                    : model),
            ],
        };

    private static async Task<string> PrintAsync(EmitPlan plan, string modelName, string payload)
    {
        // The fixture registers only the models its other tests materialize; this one needs the one it prints.
        var registered = plan with
        {
            Registry = new RegistryPlan { TypeNames = [.. plan.Registry.TypeNames.Append(modelName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] },
        };
        var assembly = await GeneratedSourceCompiler.CompileAndLoadWithSdkCoreAsync(SourceEmitter.Emit(registered));
        var modelType = assembly.GetType($"OpenCode.Sdk.Models.{modelName}", throwOnError: true)!;
        var contextType = assembly.GetType("OpenCode.Sdk.Internal.Serialization.OpenCodeJsonContext", throwOnError: true)!;
        var context = (JsonSerializerContext)(contextType.GetProperty("Default")?.GetValue(null)
                                              ?? throw new InvalidOperationException("Generated JSON context has no Default instance."));
        var typeInfo = context.GetTypeInfo(modelType)
                       ?? throw new InvalidOperationException($"Generated JSON context has no {modelName} metadata.");
        var value = JsonSerializer.Deserialize(payload, typeInfo)
                    ?? throw new InvalidOperationException("The payload materialized null.");
        return value.ToString()!;
    }
}
