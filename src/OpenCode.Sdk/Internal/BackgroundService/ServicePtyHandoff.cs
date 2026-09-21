using System.Text.Json;
using OpenCode.Sdk.Internal.BackgroundService.Abstractions;
using OpenCode.Sdk.Internal.Serialization;

namespace OpenCode.Sdk.Internal.BackgroundService;

/// <summary>
/// The pinned client's persistent-terminal handoff sidecar (<c>pty-handoff.ts:13-97</c>) over the
/// background-service filesystem seam and the generated persistent-PTY lifecycle doors. The
/// sidecar sits beside the registration at <c>&lt;registration&gt;.pty-handoff</c>; publication
/// goes through an exclusive owner-only temporary file and a replace-on-success rename in the same
/// directory, and a JSON-null handoff carries a 30-second fallback expiry. Time comes from the
/// injected clock so every expiry rule is testable without waiting.
/// </summary>
internal sealed class ServicePtyHandoff(IServiceFileSystem fileSystem, IServiceClock clock) : IServicePtyHandoff
{
    /// <summary>The sidecar suffix the pinned client keeps beside the registration.</summary>
    internal const string SidecarSuffix = ".pty-handoff";

    /// <summary>The environment variable a replacement contender reads its ticket from.</summary>
    private const string HandoffVariable = "OPENCODE_PTY_HANDOFF";

    /// <summary>Upstream's fallback expiry for a sidecar that carries no ticket (<c>pty-handoff.ts:71</c>).</summary>
    private static readonly TimeSpan NullSidecarLifetime = TimeSpan.FromSeconds(30);

    /// <summary>The pinned client's <c>Failed to prepare persistent terminals for service replacement</c>.</summary>
    internal const string PrepareFailedMessage = "Failed to prepare persistent terminals for service replacement.";

    /// <summary>The pinned client's <c>Failed to shut down persistent terminals before service replacement</c>.</summary>
    internal const string ShutdownFailedMessage = "Failed to shut down persistent terminals before service replacement.";

    /// <summary>The pinned client's <c>Invalid or expired persistent terminal handoff</c>.</summary>
    internal const string InvalidHandoffMessage = "Invalid or expired persistent terminal handoff.";

    /// <inheritdoc />
    public async Task PrepareAsync(
        string registrationFile,
        ServiceRegistration registration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        var sidecarPath = registrationFile + SidecarSuffix;
        if (await IsFreshMatchAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            using var client = CreateClient(registration);
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(timeout);
            var response = await client.PersistentPtys.HandoffAsync(cancellationToken: bound.Token).ConfigureAwait(false);
            if (response.Handoff is not { } ticket)
            {
                await PublishNullAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!IsFinite(ticket.ExpiresAt) || ticket.ExpiresAt <= NowMilliseconds())
            {
                throw new OpenCodeServerException(InvalidHandoffMessage);
            }

            var payload = JsonSerializer.SerializeToElement(ticket, OpenCodeJsonContext.Default.PersistentPtyHandoff);
            await PublishAsync(sidecarPath, NewSidecar(registration, payload, ticket.ExpiresAt), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OpenCodeApiException exception) when (exception.Status == 404)
        {
            // The route is absent (an older daemon): shut its terminals down and publish a null
            // sidecar so the replacement still transfers cleanly.
            await ShutdownThenPublishNullAsync(registration, timeout, sidecarPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OpenCodeApiException)
        {
            await RecheckThenThrowAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false);
        }
        catch (OpenCodeTransportException)
        {
            await RecheckThenThrowAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The internal request bound fired, not the caller's token.
            await RecheckThenThrowAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string?>> EnvironmentAsync(
        string registrationFile,
        IReadOnlyDictionary<string, string>? callerEnvironment,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        cancellationToken.ThrowIfCancellationRequested();

        var overlay = CopyOverlay(callerEnvironment);
        overlay[HandoffVariable] = await AdoptAsync(
                registrationFile + SidecarSuffix,
                registrationFile,
                cancellationToken)
            .ConfigureAwait(false);
        return overlay;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string registrationFile,
        ServiceRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        var sidecar = await TryReadSidecarAsync(registrationFile + SidecarSuffix, cancellationToken).ConfigureAwait(false);
        if (sidecar is not null && !Matches(sidecar, registration))
        {
            await ClearAsync(registrationFile, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(string registrationFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationFile);
        cancellationToken.ThrowIfCancellationRequested();

        var sidecarPath = registrationFile + SidecarSuffix;
        try
        {
            _ = fileSystem.TryDelete(sidecarPath);
        }
        catch (IOException exception)
        {
            throw ClearFailure(sidecarPath, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw ClearFailure(sidecarPath, exception);
        }

        return Task.CompletedTask;
    }

    private async Task ShutdownThenPublishNullAsync(
        ServiceRegistration registration,
        TimeSpan timeout,
        string sidecarPath,
        CancellationToken cancellationToken)
    {
        _ = await TryShutdownAsync(registration, timeout, cancellationToken).ConfigureAwait(false);
        await PublishNullAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Best effort: a refused shutdown still publishes the null sidecar, the way the pinned
    /// client shuts down before replacement and only throws when that shutdown itself fails.
    /// </summary>
    /// <returns>False when no daemon answered the shutdown; the caller publishes either way.</returns>
    private static async Task<bool> TryShutdownAsync(
        ServiceRegistration registration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(registration);
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(timeout);
            _ = await client.PersistentPtys.ShutdownAsync(cancellationToken: bound.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new OpenCodeServerException(ShutdownFailedMessage);
        }
        catch (OpenCodeApiException exception) when (exception.Status == 404)
        {
            // No daemon to shut down; the pinned client treats that as done.
            return false;
        }
        catch (OpenCodeException exception)
        {
            throw new OpenCodeServerException(ShutdownFailedMessage, exception);
        }
    }

    private async Task RecheckThenThrowAsync(
        string sidecarPath,
        ServiceRegistration registration,
        CancellationToken cancellationToken)
    {
        // Another caller may already have prepared and stopped this server.
        if (await IsFreshMatchAsync(sidecarPath, registration, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new OpenCodeServerException(PrepareFailedMessage);
    }

    private async Task<bool> IsFreshMatchAsync(
        string sidecarPath,
        ServiceRegistration registration,
        CancellationToken cancellationToken)
    {
        var sidecar = await TryReadSidecarAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
        return sidecar is not null && sidecar.ExpiresAt > NowMilliseconds() && Matches(sidecar, registration);
    }

    private async Task<string?> AdoptAsync(
        string sidecarPath,
        string registrationFile,
        CancellationToken cancellationToken)
    {
        var sidecar = await TryReadSidecarAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
        if (sidecar is null || sidecar.ExpiresAt <= NowMilliseconds())
        {
            return null;
        }

        if (!await RegistrationAbsentOrMatchesAsync(registrationFile, sidecar, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return sidecar.Handoff?.GetRawText();
    }

    private async Task<bool> RegistrationAbsentOrMatchesAsync(
        string registrationFile,
        PtyHandoffSidecar sidecar,
        CancellationToken cancellationToken)
    {
        // The pinned client parses the registration with no schema; a missing or undecodable file
        // reads as absent state, which the adoption rule accepts.
        var bytes = await new ServiceRegistrationFile(fileSystem)
            .TryReadBytesAsync(registrationFile, cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            return true;
        }

        var current = ServiceRegistrationReader.TryRead(bytes);
        return current is null || Matches(sidecar, current);
    }

    private async Task<PtyHandoffSidecar?> TryReadSidecarAsync(string sidecarPath, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(sidecarPath))
        {
            return null;
        }

        try
        {
            var bytes = await fileSystem.ReadAllBytesAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
            return PtyHandoffSidecar.TryRead(bytes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private Task PublishNullAsync(string sidecarPath, ServiceRegistration registration, CancellationToken cancellationToken) =>
        PublishAsync(
            sidecarPath,
            NewSidecar(registration, handoff: null, NowMilliseconds() + NullSidecarLifetime.TotalMilliseconds),
            cancellationToken);

    private async Task PublishAsync(
        string sidecarPath,
        PtyHandoffSidecar sidecar,
        CancellationToken cancellationToken)
    {
        try
        {
            var temporary = sidecarPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var created = await fileSystem
                .TryCreateExclusiveAsync(temporary, sidecar.ToUtf8Json(), cancellationToken)
                .ConfigureAwait(false);
            if (!created)
            {
                throw new OpenCodeServerException(
                    $"The temporary persistent-terminal handoff sidecar '{temporary}' already exists.");
            }

            try
            {
                fileSystem.Rename(temporary, sidecarPath);
            }
            finally
            {
                // Best effort: the temporary is a stray only when the publish failed, and it must
                // not mask that failure.
                _ = RemoveTemporary(temporary);
            }
        }
        catch (IOException exception)
        {
            throw new OpenCodeServerException(PrepareFailedMessage, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new OpenCodeServerException(PrepareFailedMessage, exception);
        }
    }

    /// <summary>
    /// Best-effort stray removal: the publish outcome is the one worth reporting, so a removal
    /// failure reads as false rather than masking it.
    /// </summary>
    /// <returns>True when the temporary is gone.</returns>
    private bool RemoveTemporary(string temporary)
    {
        try
        {
            return fileSystem.TryDelete(temporary);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static PtyHandoffSidecar NewSidecar(ServiceRegistration registration, JsonElement? handoff, double expiresAt) =>
        new()
        {
            SourceId = registration.Id,
            SourcePid = registration.ProcessId,
            SourceUrl = registration.Url,
            Handoff = handoff,
            ExpiresAt = expiresAt,
        };

    private static bool Matches(PtyHandoffSidecar sidecar, ServiceRegistration registration) =>
        string.Equals(sidecar.SourceId, registration.Id, StringComparison.Ordinal) &&
        sidecar.SourcePid == registration.ProcessId &&
        string.Equals(sidecar.SourceUrl, registration.Url, StringComparison.Ordinal);

    private static Dictionary<string, string?> CopyOverlay(IReadOnlyDictionary<string, string>? callerEnvironment)
    {
        var overlay = new Dictionary<string, string?>(callerEnvironment?.Count ?? 0, StringComparer.Ordinal);
        if (callerEnvironment is not null)
        {
            foreach (var entry in callerEnvironment)
            {
                overlay[entry.Key] = entry.Value;
            }
        }

        return overlay;
    }

    private static OpenCodeClient CreateClient(ServiceRegistration registration) =>
        new(new OpenCodeClientOptions
        {
            Endpoint = registration.Endpoint,
            Password = registration.Password,
        });

    private static OpenCodeServerException ClearFailure(string sidecarPath, Exception cause) =>
        new($"The persistent-terminal handoff sidecar '{sidecarPath}' could not be removed.", cause);

    /// <summary><c>Number.isFinite</c>: finite on every target framework, unlike <c>double.IsFinite</c>.</summary>
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private double NowMilliseconds() => clock.UtcNow.ToUnixTimeMilliseconds();
}
