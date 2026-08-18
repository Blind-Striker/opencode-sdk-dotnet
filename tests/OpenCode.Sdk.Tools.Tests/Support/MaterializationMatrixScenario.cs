namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class MaterializationMatrixScenario : SpecScenario
{
    public const string GetOperationId = "v2.matrix.get";
    public const string ChoiceOperationId = "v2.matrix.choice";

    protected override void Arrange(SpecDocumentBuilder spec) => spec
        .WithSchema("MatrixChoiceAlpha", schema => Choice(schema, "alpha"))
        .WithSchema("MatrixChoiceBeta", schema => Choice(schema, "beta"))
        .WithSchema("MatrixChoice", schema => schema.AnyOf(
            branch => branch.Ref("MatrixChoiceAlpha"),
            branch => branch.Ref("MatrixChoiceBeta")))
        .WithSchema("MatrixError", schema => schema.Type("object")
            .AdditionalPropertiesFalse()
            .Property("_tag", property => property.Type("string").Enum("MatrixError"), required: true)
            .Property("message", property => property.Type("string"), required: true))
        .WithSchema("MaterializationMatrix", Matrix)
        .WithOperation(GetOperationId, path: "/api/matrix", configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("MaterializationMatrix"))
            .Response(400, "application/json", schema => schema.Ref("MatrixError")))
        .WithOperation(ChoiceOperationId, path: "/api/matrix/choice", configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("MatrixChoice"))
            .Response(400, "application/json", schema => schema.Ref("MatrixError")));

    private static void Choice(SchemaBuilder schema, string marker) => schema.Type("object")
        .AdditionalPropertiesFalse()
        .Property("type", property => property.Type("string").Enum(marker), required: true);

    private static void Matrix(SchemaBuilder schema) => schema.Type("object")
        .AdditionalPropertiesFalse()
        .Property("requiredScalar", property => property.Type("string"), required: true)
        .Property("requiredNullableScalar", NullableString, required: true)
        .Property("optionalScalar", property => property.Type("string"))
        .Property("optionalNullableScalar", NullableString)
        .Property("requiredNumber", property => property.Type("number"), required: true)
        .Property("requiredNullableNumber", NullableNumber, required: true)
        .Property("optionalNumber", property => property.Type("number"))
        .Property("optionalNullableNumber", NullableNumber)
        .Property("requiredList", property => property.Type("array").Items(item => item.Type("string")), required: true)
        .Property("requiredNullableList", property => property.AnyOf(
            branch => branch.Type("array").Items(item => item.Type("string")),
            branch => branch.Type("null")), required: true)
        .Property("optionalList", property => property.Type("array").Items(item => item.Type("string")))
        .Property("optionalNullableList", property => property.AnyOf(
            branch => branch.Type("array").Items(item => item.Type("string")),
            branch => branch.Type("null")))
        .Property("requiredDictionary", property => property.Type("object")
            .AdditionalProperties(value => value.Type("string")), required: true)
        .Property("requiredNullableDictionary", property => property.AnyOf(
            branch => branch.Type("object").AdditionalProperties(value => value.Type("string")),
            branch => branch.Type("null")), required: true)
        .Property("optionalDictionary", property => property.Type("object")
            .AdditionalProperties(value => value.Type("string")))
        .Property("optionalNullableDictionary", property => property.AnyOf(
            branch => branch.Type("object").AdditionalProperties(value => value.Type("string")),
            branch => branch.Type("null")))
        .Property("nonnullItems", property => property.Type("array").Items(item => item.Type("string")), required: true)
        .Property("nullableItems", property => property.Type("array").Items(NullableString), required: true)
        .Property("nonnullValues", property => property.Type("object")
            .AdditionalProperties(value => value.Type("string")), required: true)
        .Property("nullableValues", property => property.Type("object")
            .AdditionalProperties(NullableString), required: true)
        .Property("requiredAny", property => property.Unrestricted(), required: true)
        .Property("requiredNullableAny", NullableAny, required: true)
        .Property("optionalAny", property => property.Unrestricted())
        .Property("anyItems", property => property.Type("array")
            .Items(item => item.Unrestricted()), required: true)
        .Property("nullableAnyItems", property => property.Type("array")
            .Items(NullableAny), required: true)
        .Property("anyValues", property => property.Type("object")
            .AdditionalProperties(value => value.Unrestricted()), required: true)
        .Property("nullableAnyValues", property => property.Type("object")
            .AdditionalProperties(NullableAny), required: true)
        .Property("freeform", property => property.Type("object").AdditionalPropertiesTrue(), required: true)
        .Property("requiredChoice", property => property.Ref("MatrixChoice"), required: true)
        .Property("choices", property => property.Type("array").Items(item => item.Ref("MatrixChoice")), required: true)
        .Property("fixedFlag", property => property.Type("boolean").BooleanEnum(true), required: true);

    private static void NullableString(SchemaBuilder schema) => schema.AnyOf(
        branch => branch.Type("string"),
        branch => branch.Type("null"));

    private static void NullableNumber(SchemaBuilder schema) => schema.AnyOf(
        branch => branch.Type("number"),
        branch => branch.Type("null"));

    private static void NullableAny(SchemaBuilder schema) => schema.AnyOf(
        branch => branch.Unrestricted(),
        branch => branch.Type("null"));
}
