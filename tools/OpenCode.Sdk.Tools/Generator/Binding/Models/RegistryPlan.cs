namespace OpenCode.Sdk.Tools.Generator.Binding.Models;

internal sealed record RegistryPlan
{
    public required IReadOnlyList<string> TypeNames
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = Array.AsReadOnly([.. value]);
        }
    } = Array.AsReadOnly(Array.Empty<string>());
}
