using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests;

public sealed class FailureClassificationTests
{
    private const string SendFaultMessage = "The opencode server could not be reached.";
    private const string SendTimeoutMessage = "The opencode server did not respond within the transport timeout.";
    private const string SendCanceledMessage = "The opencode request was canceled.";
    private const string ResponseBodyFaultMessage = "The opencode response body could not be read.";
    private const string ResponseBodyCanceledMessage = "The opencode response body read was canceled.";
    private const string EventStreamFaultMessage = "The opencode event stream could not be read.";
    private const string EventStreamCanceledMessage = "The opencode event stream read was canceled.";

    public static IEnumerable<Func<Exception>> SendFaults() =>
    [
        static () => new HttpRequestException("connection refused"),
        static () => new ObjectDisposedException(nameof(HttpClient)),
    ];

    public static IEnumerable<Func<Exception>> ResponseBodyFaults() =>
    [
        static () => new HttpRequestException("connection reset"),
        static () => new IOException("connection reset"),
        static () => new ObjectDisposedException("content"),
        static () => new InvalidOperationException("invalid charset"),
        static () => new TimeoutException(),
    ];

    public static IEnumerable<Func<Exception>> EventStreamFaults() =>
    [
        static () => new HttpRequestException("connection reset"),
        static () => new IOException("connection reset"),
        static () => new ObjectDisposedException("body"),
    ];

    public static IEnumerable<Func<(Exception Fault, bool Handled)>> SendOwnershipCases() =>
    [
        static () => (new HttpRequestException("connection refused"), true),
        static () => (new ObjectDisposedException(nameof(HttpClient)), true),
        static () => (new TaskCanceledException(), true),
        static () => (new IOException("connection reset"), false),
        static () => (new InvalidOperationException("invalid charset"), false),
        static () => (new TimeoutException(), false),
        static () => (new FormatException("caller bug"), false),
    ];

    public static IEnumerable<Func<(Exception Fault, bool Handled)>> ResponseBodyOwnershipCases() =>
    [
        static () => (new HttpRequestException("connection reset"), true),
        static () => (new IOException("connection reset"), true),
        static () => (new ObjectDisposedException("content"), true),
        static () => (new InvalidOperationException("invalid charset"), true),
        static () => (new TimeoutException(), true),
        static () => (new TaskCanceledException(), true),
        static () => (new NotSupportedException("caller bug"), false),
        static () => (new FormatException("caller bug"), false),
    ];

    public static IEnumerable<Func<(Exception Fault, bool Handled)>> EventStreamOwnershipCases() =>
    [
        static () => (new HttpRequestException("connection reset"), true),
        static () => (new IOException("connection reset"), true),
        static () => (new ObjectDisposedException("body"), true),
        static () => (new TaskCanceledException(), true),
        static () => (new InvalidOperationException("caller bug"), false),
        static () => (new TimeoutException(), false),
        static () => (new FormatException("caller bug"), false),
    ];

    [Test]
    [MethodDataSource(nameof(SendOwnershipCases))]
    public async Task Handles_Should_Own_Exactly_The_Send_Fault_Set(Exception fault, bool handled)
    {
        await Assert.That(FailureClassification.Handles(fault, FailurePhase.Send)).IsEqualTo(handled);
    }

    [Test]
    [MethodDataSource(nameof(ResponseBodyOwnershipCases))]
    public async Task Handles_Should_Own_Exactly_The_Response_Body_Fault_Set(Exception fault, bool handled)
    {
        await Assert.That(FailureClassification.Handles(fault, FailurePhase.ResponseBodyRead)).IsEqualTo(handled);
    }

    [Test]
    [MethodDataSource(nameof(EventStreamOwnershipCases))]
    public async Task Handles_Should_Own_Exactly_The_Event_Stream_Fault_Set(Exception fault, bool handled)
    {
        await Assert.That(FailureClassification.Handles(fault, FailurePhase.EventStreamRead)).IsEqualTo(handled);
    }

    [Test]
    [MethodDataSource(nameof(SendFaults))]
    public async Task Map_Should_Wrap_A_Send_Fault_As_A_Transport_Failure(Exception fault)
    {
        var mapped = FailureClassification.Map(fault, FailurePhase.Send, CancellationToken.None);

        await Assert.That(mapped).IsTypeOf<OpenCodeTransportException>();
        await Assert.That(mapped.Message).IsEqualTo(SendFaultMessage);
        await Assert.That(mapped.InnerException).IsSameReferenceAs(fault);
    }

    [Test]
    [MethodDataSource(nameof(ResponseBodyFaults))]
    public async Task Map_Should_Wrap_A_Response_Body_Fault_As_A_Transport_Failure(Exception fault)
    {
        var mapped = FailureClassification.Map(fault, FailurePhase.ResponseBodyRead, CancellationToken.None);

        await Assert.That(mapped).IsTypeOf<OpenCodeTransportException>();
        await Assert.That(mapped.Message).IsEqualTo(ResponseBodyFaultMessage);
        await Assert.That(mapped.InnerException).IsSameReferenceAs(fault);
    }

    [Test]
    [MethodDataSource(nameof(EventStreamFaults))]
    public async Task Map_Should_Wrap_An_Event_Stream_Fault_As_A_Transport_Failure(Exception fault)
    {
        var mapped = FailureClassification.Map(fault, FailurePhase.EventStreamRead, CancellationToken.None);

        await Assert.That(mapped).IsTypeOf<OpenCodeTransportException>();
        await Assert.That(mapped.Message).IsEqualTo(EventStreamFaultMessage);
        await Assert.That(mapped.InnerException).IsSameReferenceAs(fault);
    }

    [Test]
    public async Task Map_Should_Read_An_Unrequested_Send_Cancellation_As_The_Transport_Timeout()
    {
        var canceled = new TaskCanceledException();

        var mapped = FailureClassification.Map(canceled, FailurePhase.Send, CancellationToken.None);

        await Assert.That(mapped).IsTypeOf<OpenCodeTransportException>();
        await Assert.That(mapped.Message).IsEqualTo(SendTimeoutMessage);
        await Assert.That(mapped.InnerException).IsSameReferenceAs(canceled);
    }

    [Test]
    public async Task Map_Should_Read_An_Unrequested_Response_Body_Cancellation_As_A_Transport_Failure()
    {
        var canceled = new TaskCanceledException();

        var mapped = FailureClassification.Map(canceled, FailurePhase.ResponseBodyRead, CancellationToken.None);

        await Assert.That(mapped).IsTypeOf<OpenCodeTransportException>();
        await Assert.That(mapped.Message).IsEqualTo(ResponseBodyFaultMessage);
        await Assert.That(mapped.InnerException).IsSameReferenceAs(canceled);
    }

    [Test]
    public async Task Map_Should_Read_An_Unrequested_Event_Stream_Cancellation_As_A_Transport_Failure()
    {
        var canceled = new TaskCanceledException();

        var mapped = FailureClassification.Map(canceled, FailurePhase.EventStreamRead, CancellationToken.None);

        await Assert.That(mapped).IsTypeOf<OpenCodeTransportException>();
        await Assert.That(mapped.Message).IsEqualTo(EventStreamFaultMessage);
        await Assert.That(mapped.InnerException).IsSameReferenceAs(canceled);
    }

    [Test]
    [MethodDataSource(nameof(SendFaults))]
    public async Task Map_Should_Read_A_Send_Fault_As_Cancellation_When_The_Caller_Canceled(Exception fault)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var mapped = FailureClassification.Map(fault, FailurePhase.Send, cancellation.Token);

        await Assert.That(mapped).IsTypeOf<OperationCanceledException>();
        var canceled = (OperationCanceledException)mapped;
        await Assert.That(canceled.Message).IsEqualTo(SendCanceledMessage);
        await Assert.That(canceled.InnerException).IsSameReferenceAs(fault);
        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    [MethodDataSource(nameof(ResponseBodyFaults))]
    public async Task Map_Should_Read_A_Response_Body_Fault_As_Cancellation_When_The_Caller_Canceled(Exception fault)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var mapped = FailureClassification.Map(fault, FailurePhase.ResponseBodyRead, cancellation.Token);

        await Assert.That(mapped).IsTypeOf<OperationCanceledException>();
        var canceled = (OperationCanceledException)mapped;
        await Assert.That(canceled.Message).IsEqualTo(ResponseBodyCanceledMessage);
        await Assert.That(canceled.InnerException).IsSameReferenceAs(fault);
        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    [MethodDataSource(nameof(EventStreamFaults))]
    public async Task Map_Should_Read_An_Event_Stream_Fault_As_Cancellation_When_The_Caller_Canceled(Exception fault)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var mapped = FailureClassification.Map(fault, FailurePhase.EventStreamRead, cancellation.Token);

        await Assert.That(mapped).IsTypeOf<OperationCanceledException>();
        var canceled = (OperationCanceledException)mapped;
        await Assert.That(canceled.Message).IsEqualTo(EventStreamCanceledMessage);
        await Assert.That(canceled.InnerException).IsSameReferenceAs(fault);
        await Assert.That(canceled.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task Map_Should_Pass_A_Caller_Cancellation_Through_In_Every_Phase()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        FailurePhase[] phases = [FailurePhase.Send, FailurePhase.ResponseBodyRead, FailurePhase.EventStreamRead];

        foreach (var phase in phases)
        {
            var original = new OperationCanceledException(cancellation.Token);

            var mapped = FailureClassification.Map(original, phase, cancellation.Token);

            await Assert.That(mapped).IsSameReferenceAs(original);
        }
    }
}
