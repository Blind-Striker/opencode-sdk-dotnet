using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;

namespace OpenCode.Sdk.ServiceFixture;

/// <summary>
/// The discovery mode's diagnostic timeline: the runtime's own network events (HTTP request,
/// socket connect, name resolution) and the probe's start and end, each stamped with the
/// milliseconds since this process reached the mode, followed by the process's startup time and
/// CPU time. A discovery test prints the executable's stderr when it fails, so a probe that spent
/// its bound shows where the time went: a connect that started late (a starved process), a refusal
/// that arrived late (the SYN retransmissions the loopback option removes), or a connect that
/// succeeded against a port that was no longer closed.
/// </summary>
/// <remarks>
/// The lines are diagnostics, with one exception: the stale-registration test reads the socket's
/// refused-connect event as the witness of the refusal. No line contains the verdict text
/// <c>probe timedOut=</c> the tests read. Every line starts with <c>timeline</c>.
/// </remarks>
internal sealed class ProbeTimeline : EventListener
{
    private const string RequestFailedDetailed = "RequestFailedDetailed";

    private const string AddressPayload = "address";

    private static readonly string[] SourceNames = ["System.Net.Http", "System.Net.Sockets", "System.Net.NameResolution"];

    /// <summary>
    /// The moment the mode started. Like <see cref="_lines"/> it is a field initializer, which runs
    /// before the base constructor reports the event sources that already exist, so both are ready
    /// for the first <see cref="OnEventWritten"/> call.
    /// </summary>
    private readonly long _origin = Stopwatch.GetTimestamp();

    /// <summary>How long the operating system took from creating this process to the mode's start.</summary>
    private readonly TimeSpan _startup = StartupTime();

    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Records a named moment of the discovery mode itself.</summary>
    /// <param name="text">What happened.</param>
    public void Mark(string text) => _lines.Enqueue(Line(text));

    /// <summary>Renders every recorded line, then the process's startup and CPU time.</summary>
    /// <returns>The timeline, one line per event.</returns>
    public string Render()
    {
        using var process = Process.GetCurrentProcess();
        var elapsed = Stopwatch.GetElapsedTime(_origin);
        var builder = new StringBuilder();
        foreach (var line in _lines)
        {
            _ = builder.AppendLine(line);
        }

        _ = builder
            .Append("timeline process startup=")
            .Append(Milliseconds(_startup))
            .Append(" ms cpu=")
            .Append(Milliseconds(process.TotalProcessorTime))
            .Append(" ms wall=")
            .Append(Milliseconds(elapsed))
            .Append(" ms cores=")
            .Append(Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        if (Array.IndexOf(SourceNames, eventSource.Name) >= 0)
        {
            EnableEvents(eventSource, EventLevel.Informational);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // RequestFailedDetailed carries the failed request's full stack trace, which repeats the
        // failure RequestFailed already names and buries the timeline under it.
        if (eventData.EventName == RequestFailedDetailed)
        {
            return;
        }

        var text = new StringBuilder()
            .Append(eventData.EventSource.Name)
            .Append('/')
            .Append(eventData.EventName);
        if (eventData.PayloadNames is { } names && eventData.Payload is { } values)
        {
            for (var index = 0; index < names.Count && index < values.Count; index++)
            {
                // ConnectStart's address is a serialized socket address, which rendered as raw bytes
                // on a CI runner; the probe start line already names the endpoint in readable form.
                if (names[index] == AddressPayload)
                {
                    continue;
                }

                _ = text
                    .Append(' ')
                    .Append(names[index])
                    .Append('=')
                    .Append(Convert.ToString(values[index], CultureInfo.InvariantCulture));
            }
        }

        _lines.Enqueue(Line(text.ToString()));
    }

    /// <summary>The wall-clock gap between the process's creation and now: the process start time is a wall-clock value, so no monotonic clock can measure it.</summary>
    private static TimeSpan StartupTime()
    {
        using var process = Process.GetCurrentProcess();
        return DateTime.UtcNow - process.StartTime.ToUniversalTime();
    }

    private static string Milliseconds(TimeSpan span) =>
        span.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture);

    private string Line(string text) =>
        "timeline +" + Milliseconds(Stopwatch.GetElapsedTime(_origin)) + " ms " + text;
}
