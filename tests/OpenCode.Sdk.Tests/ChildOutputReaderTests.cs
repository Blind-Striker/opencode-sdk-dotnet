using System.Collections.Concurrent;
using System.IO.Pipes;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.Tests.Support;
using TUnit.Assertions.Enums;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// <see cref="ChildOutputReader"/> over an anonymous pipe of this process, the same synchronous
/// pipe kind <see cref="System.Diagnostics.Process"/> creates for a redirected stream on Windows
/// before .NET 11: every line arrives on the reader's own thread, never a thread-pool thread, and
/// a read blocked on a silent pipe ends when the owner cancels it.
/// </summary>
public sealed class ChildOutputReaderTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Start_Should_Deliver_Every_Line_Off_The_Thread_Pool_And_Report_End_Of_Stream()
    {
        var lines = new ConcurrentQueue<(string Line, bool OnPool)>();
        using var server = new AnonymousPipeServerStream(PipeDirection.Out);
        using var client = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle);
        using var reader = new StreamReader(client);

        var outputReader = ChildOutputReader.Start(
            reader,
            line => lines.Enqueue((line, Thread.CurrentThread.IsThreadPoolThread)),
            "child-output-reader-test");
        using (var writer = new StreamWriter(server))
        {
            await writer.WriteLineAsync("first");
            await writer.WriteLineAsync("second");
        }

        var endOfStream = await outputReader.Completion.WaitAsync(Bound);

        await Assert.That(endOfStream).IsTrue();
        await Assert.That(lines.Select(static entry => entry.Line)).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
        await Assert.That(lines.Any(static entry => entry.OnPool)).IsFalse();
    }

    [Test]
    [SlopwatchSuppress("SW004", "The delay is the cancel retry cadence the owner uses: a cancel landing between two reads finds nothing to cancel and is repeated")]
    public async Task CancelPendingRead_Should_End_A_Read_Blocked_On_A_Silent_Pipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            // CancelSynchronousIo is the Windows mechanism; elsewhere the launcher's reads are
            // asynchronous and hold no thread, so there is no blocked read to end.
            return;
        }

        using var server = new AnonymousPipeServerStream(PipeDirection.Out);
        using var client = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle);
        using var reader = new StreamReader(client);
        var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outputReader = ChildOutputReader.Start(reader, _ => delivered.TrySetResult(true), "child-output-reader-test");

        // One line proves the thread is reading; after it, the write end stays open and silent, so
        // the thread blocks in its next ReadFile and only a cancel of that read can end it. The
        // short wait below gives the thread time to enter that read, so a cancel that only set
        // the stop flag, without canceling the blocked read, would leave the reader running.
        using var writer = new StreamWriter(server) { AutoFlush = true };
        await writer.WriteLineAsync("ready");
        _ = await delivered.Task.WaitAsync(Bound);
        var stillReading = await Task.WhenAny(outputReader.Completion, Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None))
                           != outputReader.Completion;
        await Assert.That(stillReading).IsTrue();

        using var deadline = new CancellationTokenSource(Bound);
        while (!outputReader.Completion.IsCompleted && !deadline.IsCancellationRequested)
        {
            outputReader.CancelPendingRead();
            _ = await Task.WhenAny(outputReader.Completion, Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None));
        }

        await Assert.That(outputReader.Completion.IsCompleted).IsTrue();
        await Assert.That(await outputReader.Completion).IsFalse();
    }
}
