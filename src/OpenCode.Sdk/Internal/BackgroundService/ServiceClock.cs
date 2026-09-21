using OpenCode.Sdk.Internal.BackgroundService.Abstractions;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>The production <see cref="IServiceClock"/> over <see cref="DateTimeOffset.UtcNow"/>.</summary>
internal sealed class ServiceClock : IServiceClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
