using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using OpenCode.Sdk.TestSupport.Abstractions;

namespace OpenCode.Sdk.TestSupport;

internal sealed class GitProcess : IGitProcess
{
    private const string CleanupFailuresKey = "GitProcess.CleanupFailures";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task RunAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };

        Start(process, workingDirectory, arguments);
        // The readers keep draining while cancellation tears down the owned process. Giving them
        // the caller token could stop consumption before the process exits and fill a pipe.
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await StopAfterCancellationAsync(process, exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "git " + arguments + " failed with exit code " +
                process.ExitCode.ToString(CultureInfo.InvariantCulture) + " in '" + workingDirectory + "'." +
                Environment.NewLine + "stdout:" + Environment.NewLine + output +
                Environment.NewLine + "stderr:" + Environment.NewLine + error);
        }
    }

    private static void Start(Process process, string workingDirectory, string arguments)
    {
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The Git process did not start.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"git {arguments} could not start in '{workingDirectory}'.", exception);
        }
    }

    private static async Task StopAfterCancellationAsync(
        Process process,
        OperationCanceledException primary)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (Exception exception)
        {
            primary.Data[CleanupFailuresKey] = new AggregateException(exception);
        }
    }
}
