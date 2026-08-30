using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class ErrorMarkerPolicyTests
{
    [Test]
    [Arguments(ErrorStyle.EffectTag, "_tag")]
    [Arguments(ErrorStyle.NameData, "name")]
    public async Task TryGetWireName_Should_Map_An_Admitted_Dialect_To_Its_Wire_Property(ErrorStyle style, string wireName)
    {
        var found = ErrorMarkerPolicy.TryGetWireName(style, out var resolved);

        await Assert.That(found).IsTrue();
        await Assert.That(resolved).IsEqualTo(wireName);
    }

    [Test]
    public async Task ScanOrder_Should_Read_The_Effect_Dialect_Before_The_Name_Dialect()
    {
        await Assert.That(ErrorMarkerPolicy.ScanOrder.SequenceEqual(["_tag", "name"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TryGetWireName_Should_Refuse_A_Style_That_Declares_No_Marker()
    {
        var found = ErrorMarkerPolicy.TryGetWireName(ErrorStyle.None, out var resolved);

        await Assert.That(found).IsFalse();
        await Assert.That(resolved).IsNull();
    }

    [Test]
    public async Task Resolve_Should_Refuse_A_Style_That_Declares_No_Marker_By_Name()
    {
        var node = Error(ErrorStyle.None, new LiteralMarker
        {
            PropertyName = "kind",
            Kind = LiteralKind.String,
            Value = "Boom",
        });

        var marker = ErrorMarkerPolicy.Resolve(node, out var problem);

        await Assert.That(marker).IsNull();
        await Assert.That(problem).IsEqualTo("error style 'None' declares no tag marker property");
    }

    [Test]
    public async Task Resolve_Should_Refuse_A_Dialect_Whose_Marker_Literal_Is_Absent()
    {
        var node = Error(ErrorStyle.NameData, new LiteralMarker
        {
            PropertyName = "_tag",
            Kind = LiteralKind.String,
            Value = "Boom",
        });

        var marker = ErrorMarkerPolicy.Resolve(node, out var problem);

        await Assert.That(marker).IsNull();
        await Assert.That(problem).IsEqualTo("a tagged error must declare exactly one required 'name' literal");
    }

    [Test]
    public async Task Resolve_Should_Return_The_Dialect_Marker_It_Declares()
    {
        var node = Error(
            ErrorStyle.NameData,
            new LiteralMarker
            {
                PropertyName = "name",
                Kind = LiteralKind.String,
                Value = "WorktreeError",
            });

        var marker = ErrorMarkerPolicy.Resolve(node, out var problem);

        await Assert.That(marker!.PropertyName).IsEqualTo("name");
        await Assert.That(marker.Value).IsEqualTo("WorktreeError");
        await Assert.That(problem).IsEmpty();
    }

    private static ObjectNode Error(ErrorStyle style, params LiteralMarker[] markers) =>
        new()
        {
            Properties = [],
            AdditionalProperties = AdditionalPropertiesKind.Forbidden,
            LiteralMarkers = markers,
            ErrorStyle = style,
        };
}
