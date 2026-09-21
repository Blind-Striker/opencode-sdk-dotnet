using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// Dilim-1 production adapter for <see cref="IServicePtyHandoff"/>: no sidecar I/O. Prepare,
/// complete, and clear are no-ops; environment returns the caller overlay unchanged. Dilim-2
/// replaces this with the pinned client's sidecar.
/// </summary>
internal sealed class ServicePtyHandoff : IServicePtyHandoff
{
    /// <inheritdoc />
    public Task PrepareAsync(
        string registrationFile,
        ServiceRegistration registration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string?>> EnvironmentAsync(
        string registrationFile,
        IReadOnlyDictionary<string, string>? callerEnvironment,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        cancellationToken.ThrowIfCancellationRequested();

        if (callerEnvironment is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string?>>(
                new Dictionary<string, string?>(StringComparer.Ordinal));
        }

        var overlay = new Dictionary<string, string?>(callerEnvironment.Count, StringComparer.Ordinal);
        foreach (var entry in callerEnvironment)
        {
            overlay[entry.Key] = entry.Value;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string?>>(overlay);
    }

    /// <inheritdoc />
    public Task CompleteAsync(
        string registrationFile,
        ServiceRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(string registrationFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
