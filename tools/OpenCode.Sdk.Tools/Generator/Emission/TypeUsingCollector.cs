using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Emission;

/// <summary>Namespaces a rendered type reference needs, gathered from the bound plan.</summary>
internal static class TypeUsingCollector
{
    public static void Collect(TypeReferencePlan type, ISet<string> usings)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(usings);

        switch (type)
        {
            case NamedTypeReferencePlan { Name: "Uri" }:
                _ = usings.Add("System");
                break;
            case NamedTypeReferencePlan { Name: "JsonElement" }:
                _ = usings.Add("System.Text.Json");
                break;
            case BinaryTypeReferencePlan:
                _ = usings.Add("System");
                break;
            case ListTypeReferencePlan list:
                _ = usings.Add("System.Collections.Generic");
                Collect(list.ElementType, usings);
                break;
            case DictionaryTypeReferencePlan dictionary:
                _ = usings.Add("System.Collections.Generic");
                Collect(dictionary.ValueType, usings);
                break;
        }
    }
}
