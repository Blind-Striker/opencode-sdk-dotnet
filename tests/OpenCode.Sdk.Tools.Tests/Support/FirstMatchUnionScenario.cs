namespace OpenCode.Sdk.Tools.Tests.Support;

/// <summary>
/// The three same-token shapes the pinned document carries, reduced to their mechanics: a text
/// arm beside two objects told apart by required keys (configuration references), a boolean
/// literal sentinel beside an object that declares the same key as a plain boolean (language
/// server entries), and a status sentinel whose first member admits two literals (the V1
/// migration status).
/// </summary>
internal sealed class FirstMatchUnionScenario : SpecScenario
{
    public const string GroupName = "first";
    public const string OperationId = "first.get";

    protected override void Arrange(SpecDocumentBuilder spec) => _ = spec
        .WithSchema("GitSource", schema => schema
            .Type("object")
            .Property("repository", property => property.Type("string"), required: true)
            .Property("branch", property => property.Type("string")))
        .WithSchema("LocalSource", schema => schema
            .Type("object")
            .Property("path", property => property.Type("string"), required: true))
        .WithSchema("SourceEntry", schema => schema.AnyOf(
            branch => branch.Type("string"),
            branch => branch.Ref("GitSource"),
            branch => branch.Ref("LocalSource")))
        .WithSchema("ToolOff", schema => schema
            .Type("object")
            .Property("disabled", property => property.Type("boolean").BooleanEnum(true), required: true))
        .WithSchema("ToolServer", schema => schema
            .Type("object")
            .Property("command", property => property.Type("array").Items(item => item.Type("string")), required: true)
            .Property("disabled", property => property.Type("boolean")))
        .WithSchema("ToolEntry", schema => schema.AnyOf(
            branch => branch.Ref("ToolOff"),
            branch => branch.Ref("ToolServer")))
        .WithSchema("JobSettled", schema => schema
            .Type("object")
            .Property("status", property => property.Type("string").Enum("required", "completed"), required: true))
        .WithSchema("JobRunning", schema => schema
            .Type("object")
            .Property("status", property => property.Type("string").Enum("running"), required: true)
            .Property("progress", property => property.Type("string"), required: true))
        .WithSchema("JobFailed", schema => schema
            .Type("object")
            .Property("status", property => property.Type("string").Enum("error"), required: true)
            .Property("error", property => property.Type("string"), required: true))
        .WithSchema("JobStatus", schema => schema.AnyOf(
            branch => branch.Ref("JobSettled"),
            branch => branch.Ref("JobRunning"),
            branch => branch.Ref("JobFailed")))
        .WithSchema("FirstContainer", schema => schema
            .Type("object")
            .Property("source", property => property.Ref("SourceEntry"), required: true)
            .Property("tool", property => property.Ref("ToolEntry"), required: true)
            .Property("status", property => property.Ref("JobStatus"), required: true))
        .WithSchema("FirstBadRequestError", schema => schema
            .Type("object")
            .Property("_tag", property => property.Type("string").Enum("FirstBadRequestError"), required: true)
            .Property("message", property => property.Type("string"), required: true))
        .WithOperation(OperationId, configure: operation => operation
            .Response(200, "application/json", schema => schema.Ref("FirstContainer"))
            .Response(400, "application/json", schema => schema.Ref("FirstBadRequestError")));
}
