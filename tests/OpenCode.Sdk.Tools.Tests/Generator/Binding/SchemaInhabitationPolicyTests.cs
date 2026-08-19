using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class SchemaInhabitationPolicyTests
{
    [Test]
    public async Task IsInhabited_Should_Reject_An_Object_With_A_Required_Never_Member()
    {
        var policy = new SchemaInhabitationPolicy(new Dictionary<string, SchemaNode>(StringComparer.Ordinal));

        await Assert.That(policy.IsInhabited(ObjectWithNeverMember(isRequired: true))).IsFalse();
    }

    [Test]
    public async Task IsInhabited_Should_Admit_An_Object_With_An_Optional_Never_Member()
    {
        var policy = new SchemaInhabitationPolicy(new Dictionary<string, SchemaNode>(StringComparer.Ordinal));

        await Assert.That(policy.IsInhabited(ObjectWithNeverMember(isRequired: false))).IsTrue();
    }

    [Test]
    public async Task IsInhabited_Should_Admit_A_Union_With_One_Inhabited_Branch()
    {
        var policy = new SchemaInhabitationPolicy(new Dictionary<string, SchemaNode>(StringComparer.Ordinal));
        var schema = new UnionNode
        {
            Branches = [new NeverNode(), new PrimitiveNode { Kind = PrimitiveKind.String, },],
            Classification = UnionClassification.Structural,
            Keyword = UnionKeyword.AnyOf,
        };

        await Assert.That(policy.IsInhabited(schema)).IsTrue();
    }

    private static ObjectNode ObjectWithNeverMember(bool isRequired) =>
        new()
        {
            Properties = [new SpecProperty { Name = "value", Schema = new NeverNode(), IsRequired = isRequired, },],
            AdditionalProperties = AdditionalPropertiesKind.Forbidden,
            LiteralMarkers = [],
            ErrorStyle = ErrorStyle.None,
        };
}
