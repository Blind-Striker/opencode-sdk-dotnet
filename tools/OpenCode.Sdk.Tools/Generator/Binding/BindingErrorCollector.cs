using OpenCode.Sdk.Tools.Generator.Binding.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class BindingErrorCollector
{
    private readonly List<BindingError> _errors = [];

    public int Count => _errors.Count;

    public void Add(BindingErrorCategory category, string subject, string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);

        _errors.Add(new BindingError(category, subject, problem));
    }

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new BindingException(_errors);
        }
    }
}
