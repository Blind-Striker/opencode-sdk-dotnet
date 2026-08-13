namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record HandleFactoryPlan
{
    public required string MethodName { get; init; }

    public required string HandleTypeName { get; init; }

    public required OperationParameterPlan Parameter { get; init; }
}
