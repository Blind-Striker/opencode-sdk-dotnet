using System.Collections;
using System.Collections.Frozen;
using System.Reflection;

namespace OpenCode.Sdk.Tools.Generator.Ingestion.Walls;

internal sealed class HostMemberWhitelist<T>
    where T : class, new()
{
    private readonly T _defaultValue = new();
    private readonly FrozenSet<string> _exemptMembers;
    private readonly string _hostName;
    private readonly PropertyInfo[] _members;

    public HostMemberWhitelist(string hostName, IEnumerable<string> exemptMembers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(exemptMembers);

        _hostName = hostName;
        _exemptMembers = exemptMembers.ToFrozenSet(StringComparer.Ordinal);
        _members =
        [
            .. typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(static property => property.Name, StringComparer.Ordinal),
        ];
    }

    public void Check(T value, string location, IngestionErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(errors);

        foreach (var member in _members)
        {
            if (_exemptMembers.Contains(member.Name) || !IsPopulated(member, value))
            {
                continue;
            }

            errors.Add(location, $"{_hostName} member '{GetWireName(member.Name)}' is not supported");
        }
    }

    private static string GetWireName(string memberName) =>
        $"{char.ToLowerInvariant(memberName[0])}{memberName[1..]}";

    private static bool IsCollection(Type type) =>
        type != typeof(string)
        && typeof(IEnumerable).IsAssignableFrom(type);

    private bool IsPopulated(PropertyInfo member, T value)
    {
        var memberValue = member.GetValue(value);
        if (memberValue is null || Equals(memberValue, member.GetValue(_defaultValue)))
        {
            return false;
        }

        if (!IsCollection(member.PropertyType))
        {
            return true;
        }

        var enumerator = ((IEnumerable)memberValue).GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
