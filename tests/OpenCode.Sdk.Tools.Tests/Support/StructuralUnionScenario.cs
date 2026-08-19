namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class StructuralUnionScenario : SpecScenario
{
    public const string GroupName = "structural";
    public const string OperationId = "v2.structural.get";

    protected override void Arrange(SpecDocumentBuilder spec) => _ = spec
        .WithSchema("StructuralValue", schema => schema.AnyOf(
            branch => branch.Type("string"),
            branch => branch.AnyOf(
                value => value.Type("number"),
                value => value.Type("string").Enum("NaN"),
                value => value.Type("string").Enum("Infinity"),
                value => value.Type("string").Enum("-Infinity")),
            branch => branch.Type("boolean"),
            branch => branch.Type("array").Items(item => item.Type("string"))))
        .WithSchema("StructuralContainer", schema => schema
            .Type("object")
            .Property("value", property => property.Ref("StructuralValue"), required: true))
        .WithSchema("StructuralBadRequestError", schema => schema
            .Type("object")
            .Property("_tag", property => property.Type("string").Enum("StructuralBadRequestError"), required: true)
            .Property("message", property => property.Type("string"), required: true))
        .WithOperation(OperationId, configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("StructuralContainer"))
            .Response(400, "application/json", schema => schema.Ref("StructuralBadRequestError")));
}
