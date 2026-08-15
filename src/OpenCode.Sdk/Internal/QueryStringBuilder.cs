using System.Globalization;
using System.Text;

namespace OpenCode.Sdk.Internal;

/// <summary>
/// Composes the query-string suffix of a generated route; unset optionals are omitted and
/// every value is escaped. Route builders own the wire conversion rules, so refusals here
/// are the sealed route-boundary contract.
/// </summary>
internal sealed class QueryStringBuilder
{
    private StringBuilder? _builder;

    public string Value => _builder?.ToString() ?? string.Empty;

    public void AddText(string name, string? value)
    {
        if (value is not null)
        {
            Append(name, value);
        }
    }

    public void AddCount(string name, int? value)
    {
        if (value is null)
        {
            return;
        }

        if (value <= 0)
        {
            throw new ArgumentException($"The '{name}' query value must be positive.", nameof(value));
        }

        Append(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    public void AddOrder(string name, ListOrder? value)
    {
        if (value is null)
        {
            return;
        }

        var wire = value switch
        {
            ListOrder.Ascending => "asc",
            ListOrder.Descending => "desc",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ListOrder value."),
        };
        Append(name, wire);
    }

    public void AddParentFilter(string name, SessionParentFilter? value)
    {
        if (value is not null)
        {
            Append(name, value.WireValue);
        }
    }

    private void Append(string name, string value)
    {
        _builder ??= new StringBuilder();
        _ = _builder.Append(_builder.Length is 0 ? '?' : '&')
            .Append(Uri.EscapeDataString(name))
            .Append('=')
            .Append(Uri.EscapeDataString(value));
    }
}
