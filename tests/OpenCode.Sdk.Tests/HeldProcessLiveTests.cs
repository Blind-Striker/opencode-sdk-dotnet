using System.Diagnostics;
using System.Globalization;
using OpenCode.Sdk.Internal;
using OpenCode.Sdk.TestSupport;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tests;

/// <summary>
/// The end a run root's teardown waits for: a bun process that holds a SQLite database in WAL mode
/// and a few hundred open files — the handle load a source-run daemon carries — is ended, and once
/// the wait returns every file it held can be deleted at once. On Windows the exit code is set
/// before those handles close; a wait that stopped there let a run root's deletion leave the dying
/// daemon's files behind.
/// </summary>
[NotInParallel]
public sealed class HeldProcessLiveTests
{
    private const int Rounds = 10;
    private static readonly TimeSpan TerminationBound = TimeSpan.FromSeconds(15);
    private readonly RealFileSystem _fileSystem = new();

    [Test]
    [Timeout(180_000)]
    public async Task EndAllAsync_Should_Return_Once_The_Ended_Process_Holds_No_File(CancellationToken cancellationToken)
    {
        var bun = new ExecutableResolver(ExecutableSearchEnvironment.ForCurrentProcess())
            .Resolve(new PinnedServerCommand(_fileSystem).Resolve()[0])
            .Path;
        var directory = _fileSystem.Path.Combine(
            _fileSystem.Path.GetTempPath(), "opencode-sdk-held-process-" + Guid.NewGuid().ToString("N"));
        _ = _fileSystem.Directory.CreateDirectory(directory);

        try
        {
            for (var round = 0; round < Rounds; round++)
            {
                var number = round.ToString(CultureInfo.InvariantCulture);
                var database = _fileSystem.Path.Combine(directory, "round-" + number + ".db");
                using var child = Process.Start(new ProcessStartInfo(bun)
                {
                    ArgumentList = { "-e", HoldingScript(database) },
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                })!;
                await Assert.That(await child.StandardOutput.ReadLineAsync(cancellationToken)).IsEqualTo("ready");

                var mark = ProcessMark.TryRead(_fileSystem, child.Id);
                await Assert.That(mark).IsNotNull();

                await Assert.That(await HeldProcess.EndAllAsync(_fileSystem, [mark!.Value], TerminationBound)).IsTrue();
                await Assert.That(TryDelete(database + "-wal") && TryDelete(database))
                    .IsTrue().Because("round " + number + ": the ended process still held its files");
            }
        }
        finally
        {
            _ = BestEffortDelete.TryDeleteTree(_fileSystem, directory);
        }
    }

    /// <summary>A bun program that opens the database in WAL mode, writes to it, holds three hundred more files, and waits.</summary>
    private static string HoldingScript(string database)
    {
        var path = database.Replace('\\', '/');
        return "const { Database } = require('bun:sqlite');"
            + $"const db = new Database('{path}'); db.exec('PRAGMA journal_mode=WAL'); db.exec('create table t(x)');"
            + "for (let i = 0; i < 2000; i++) db.exec('insert into t values(' + i + ')');"
            + $"const fs = require('fs'); for (let i = 0; i < 300; i++) fs.openSync('{path}.' + i, 'w');"
            + "console.log('ready'); setInterval(() => {}, 1000);";
    }

    private bool TryDelete(string path)
    {
        try
        {
            _fileSystem.File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
