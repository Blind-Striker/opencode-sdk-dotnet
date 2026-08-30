using System.Text;
using System.Text.Json;
using OpenCode.Sdk.Tools.Generator.Refresh;
using OpenCode.Sdk.Tools.Generator.Refresh.Models;
using OpenCode.Sdk.Tools.Serialization;
using OpenCode.Sdk.Tools.Tests.Support;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tools.Tests.Generator.Refresh;

public sealed class SnapshotSynchronizerTests
{
    private static readonly byte[] AcceptedDocument = RefreshScenarioData.DocumentBytes(spec => spec
        .WithOperation("v2.alpha.get", path: "/api/alpha")
        .WithOperation("v2.beta.get", path: "/api/beta"));

    private static readonly byte[] CandidateDocument = RefreshScenarioData.DocumentBytes(spec => spec
        .WithOperation("v2.beta.get", path: "/api/beta")
        .WithOperation("v2.gamma.get", path: "/api/gamma"));

    [Test]
    public async Task Prepare_Should_Produce_An_Identity_Receipt_Without_Patches()
    {
        var fileSystem = await CreateRepositoryAsync();
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", "show", ScriptedProcessRunner.Ok(CandidateDocument));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.PrepareAsync("origin/v2", CancellationToken.None);

        var receipt = outcome.Receipt;
        await Assert.That(receipt.UpstreamCommit).IsEqualTo(RefreshScenarioData.Commit);
        await Assert.That(receipt.AddedOperations).IsEquivalentTo(["v2.gamma.get"]);
        await Assert.That(receipt.RemovedOperations).IsEquivalentTo(["v2.alpha.get"]);
        await Assert.That(receipt.Patches).IsEmpty();
        await Assert.That(receipt.GeneratedBaselineSha256).IsNull();
        await Assert.That(receipt.NormalizedDocumentSha256).IsEqualTo(DocumentInspector.Sha256Hex(CandidateDocument));
        await Assert
            .That(await fileSystem.File.ReadAllBytesAsync(outcome.NormalizedDocumentPath, CancellationToken.None))
            .IsEquivalentTo(CandidateDocument);
        await Assert.That(fileSystem.File.Exists(outcome.ReceiptPath)).IsTrue();
        await Assert.That(runner.Invocations.Any(static invocation => invocation.StartsWith("bun", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Prepare_Should_Refuse_A_Satisfied_Repair_Predicate()
    {
        var carrying = RefreshScenarioData.DocumentBytes(spec => spec
            .WithOperation("v2.beta.get", path: "/api/beta")
            .WithSchema("Plain", schema => schema.Type("string"))
            .WithSchema("V2EventEncoded", schema => schema
                .Type("string")
                .ContentSchema("application/json", payload => payload.Ref("Plain"))));
        var fileSystem = await CreateRepositoryAsync();
        await WritePatchSetAsync(fileSystem);
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", "show", ScriptedProcessRunner.Ok(carrying));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var exception = await Assert
            .That(async () => _ = await synchronizer.PrepareAsync("origin/v2", CancellationToken.None))
            .Throws<SnapshotRefreshException>();

        await Assert.That(exception!.Message).Contains("retire the patch");
    }

    [Test]
    public async Task Prepare_Should_Run_The_Pinned_Generator_Over_Patches()
    {
        var raw = RefreshScenarioData.DocumentBytes(spec => spec
            .WithOperation("v2.beta.get", path: "/api/beta")
            .WithSchema("V2EventEncoded", schema => schema.Type("string")));
        var baseline = raw;
        var normalized = CandidateDocument;
        var fileSystem = await CreateRepositoryAsync();
        await WritePatchSetAsync(fileSystem);
        var worktree = fileSystem.Path.GetFullPath(
            fileSystem.Path.Combine(SnapshotPaths.ScratchRoot, RefreshScenarioData.Commit, "worktree"));
        var touched = fileSystem.Path.Combine(worktree, "packages/protocol/script/generate-openapi.ts");
        var artifact = fileSystem.Path.Combine(worktree, SnapshotPaths.UpstreamArtifact);
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", "show", ScriptedProcessRunner.Ok(raw))
            .Expect("git", "worktree add", sideEffect: async () =>
            {
                _ = fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(touched)!);
                await fileSystem.File.WriteAllTextAsync(touched, "before");
            })
            .Expect("bun", "install", sideEffect: () =>
            {
                _ = fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(artifact)!);
                return Task.CompletedTask;
            })
            .Expect("bun", "run generate", sideEffect: () => fileSystem.File.WriteAllBytesAsync(artifact, baseline))
            .Expect("git", "apply")
            .Expect("bun", "run generate", sideEffect: () => fileSystem.File.WriteAllBytesAsync(artifact, normalized))
            .Expect("git", "worktree remove");
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.PrepareAsync("origin/v2", CancellationToken.None);

        var receipt = outcome.Receipt;
        await Assert.That(receipt.GeneratedBaselineSha256).IsEqualTo(DocumentInspector.Sha256Hex(baseline));
        await Assert.That(receipt.NormalizedDocumentSha256).IsEqualTo(DocumentInspector.Sha256Hex(normalized));
        var preimage = receipt.Patches.Single().Preimages.Single();
        await Assert.That(preimage.Path).IsEqualTo("packages/protocol/script/generate-openapi.ts");
        await Assert.That(preimage.Sha256).IsEqualTo(DocumentInspector.Sha256Hex(Encoding.UTF8.GetBytes("before")));
        await Assert
            .That(await fileSystem.File.ReadAllBytesAsync(outcome.NormalizedDocumentPath, CancellationToken.None))
            .IsEquivalentTo(normalized);
    }

    [Test]
    public async Task Apply_Should_Refuse_Time_Of_Check_Drift()
    {
        var fileSystem = await CreateRepositoryAsync();
        var receipt = CreateReceipt(CandidateDocument, "scratch/openapi.json");
        _ = fileSystem.Directory.CreateDirectory("scratch");
        await fileSystem.File.WriteAllBytesAsync("scratch/openapi.json", AcceptedDocument);
        await fileSystem.File.WriteAllTextAsync("scratch/receipt.json", RefreshScenarioData.Serialize(receipt));
        var synchronizer = CreateSynchronizer(fileSystem, new ScriptedProcessRunner());

        var exception = await Assert
            .That(async () => _ = await synchronizer.ApplyAsync("scratch/receipt.json", CancellationToken.None))
            .Throws<SnapshotRefreshException>();

        await Assert.That(exception!.Message).Contains("re-run prepare");
    }

    [Test]
    public async Task Apply_Should_Install_The_Accepted_Snapshot_Paths()
    {
        var fileSystem = await CreateRepositoryAsync();
        var receipt = CreateReceipt(CandidateDocument, "scratch/openapi.json");
        _ = fileSystem.Directory.CreateDirectory("scratch");
        await fileSystem.File.WriteAllBytesAsync("scratch/openapi.json", CandidateDocument);
        await fileSystem.File.WriteAllTextAsync("scratch/receipt.json", RefreshScenarioData.Serialize(receipt));
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse --verify")
            .Expect("git", "checkout --detach");
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var applied = await synchronizer.ApplyAsync("scratch/receipt.json", CancellationToken.None);

        await Assert.That(applied.NormalizedDocumentPath).IsNull();
        await Assert
            .That(await fileSystem.File.ReadAllBytesAsync(SnapshotPaths.AcceptedDocument, CancellationToken.None))
            .IsEquivalentTo(CandidateDocument);
        var committed = JsonSerializer.Deserialize(
            await fileSystem.File.ReadAllTextAsync(SnapshotPaths.CommittedReceipt, CancellationToken.None),
            ToolJsonContext.Default.SnapshotReceipt);
        await Assert.That(committed!.NormalizedDocumentPath).IsNull();
        var snapshotMarkdown = await fileSystem.File.ReadAllTextAsync(SnapshotPaths.SnapshotMarkdown, CancellationToken.None);
        await Assert.That(snapshotMarkdown).Contains($"| Commit | `{RefreshScenarioData.Commit}` |");
        await Assert.That(snapshotMarkdown).DoesNotContain("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        await Assert.That(snapshotMarkdown).DoesNotContain("Date: 2026-08-13");
        await Assert
            .That(runner.Invocations.Any(invocation =>
                invocation.StartsWith($"git checkout --detach {RefreshScenarioData.Commit}", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Verify_Should_Report_A_Missing_Receipt()
    {
        var synchronizer = CreateSynchronizer(await CreateRepositoryAsync(), new ScriptedProcessRunner());

        var outcome = await synchronizer.VerifyAsync(CancellationToken.None);

        await Assert.That(outcome.IsReproduced).IsFalse();
        await Assert.That(outcome.Problems.Single()).Contains("no committed receipt");
    }

    [Test]
    public async Task Verify_Should_Reproduce_A_Matching_State()
    {
        var fileSystem = await CreateRepositoryAsync();
        await fileSystem.File.WriteAllBytesAsync(SnapshotPaths.AcceptedDocument, CandidateDocument);
        await fileSystem.File.WriteAllTextAsync(
            SnapshotPaths.CommittedReceipt,
            RefreshScenarioData.Serialize(CreateReceipt(CandidateDocument, normalizedDocumentPath: null)));
        var runner = new ScriptedProcessRunner()
            .Expect("git", "rev-parse HEAD", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.VerifyAsync(CancellationToken.None);

        await Assert.That(outcome.Problems).IsEmpty();
        await Assert.That(outcome.IsReproduced).IsTrue();
    }

    [Test]
    public async Task Verify_Should_Report_Document_And_Checkout_Mismatches()
    {
        var fileSystem = await CreateRepositoryAsync();
        await fileSystem.File.WriteAllTextAsync(
            SnapshotPaths.CommittedReceipt,
            RefreshScenarioData.Serialize(CreateReceipt(CandidateDocument, normalizedDocumentPath: null)));
        var runner = new ScriptedProcessRunner()
            .Expect("git", "rev-parse HEAD", ScriptedProcessRunner.Ok(new string('b', 40) + "\n"));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.VerifyAsync(CancellationToken.None);

        await Assert.That(outcome.Problems.Any(static problem => problem.Contains("hash", StringComparison.Ordinal))).IsTrue();
        await Assert.That(outcome.Problems.Any(static problem => problem.Contains("checkout", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Prepare_Should_Record_Every_Watched_Source_In_The_Receipt()
    {
        const string handler = "closeAccepted(new Socket.CloseEvent(4404, \"session not found\"))";
        var fileSystem = await CreateRepositoryAsync();
        var pinned = RefreshScenarioData.Watched("packages/server/src/handlers/pty.ts", handler, "4404");
        await WriteSourceWatchAsync(fileSystem, pinned);
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", $"show {RefreshScenarioData.Commit}:{SnapshotPaths.UpstreamArtifact}",
                ScriptedProcessRunner.Ok(CandidateDocument))
            .Expect("git", $"show {RefreshScenarioData.Commit}:{pinned.Path}", ScriptedProcessRunner.Ok(handler));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.PrepareAsync("origin/v2", CancellationToken.None);

        var watched = outcome.Receipt.WatchedSources.Single();
        await Assert.That(watched.Path).IsEqualTo(pinned.Path);
        await Assert.That(watched.Sha256).IsEqualTo(pinned.Sha256);
        await Assert.That(watched.AnchorMatched).IsTrue();
    }

    [Test]
    public async Task Prepare_Should_Refuse_A_Watched_Source_The_Candidate_Lost()
    {
        var fileSystem = await CreateRepositoryAsync();
        var pinned = RefreshScenarioData.Watched("packages/core/src/pty/ticket.ts", "Duration.seconds(60)", "seconds(60)");
        await WriteSourceWatchAsync(fileSystem, pinned);
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", $"show {RefreshScenarioData.Commit}:{SnapshotPaths.UpstreamArtifact}",
                ScriptedProcessRunner.Ok(CandidateDocument))
            .Expect("git", $"show {RefreshScenarioData.Commit}:{pinned.Path}",
                ScriptedProcessRunner.Fail("fatal: path does not exist"));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var exception = await Assert
            .That(async () => _ = await synchronizer.PrepareAsync("origin/v2", CancellationToken.None))
            .Throws<SnapshotRefreshException>();

        await Assert.That(exception!.Message).Contains("watched source 'packages/core/src/pty/ticket.ts' cannot be read");
    }

    [Test]
    public async Task Verify_Should_Report_A_Watched_Source_That_Moved()
    {
        var fileSystem = await CreateRepositoryAsync();
        await fileSystem.File.WriteAllBytesAsync(SnapshotPaths.AcceptedDocument, CandidateDocument);
        var pinned = RefreshScenarioData.Watched("packages/core/src/pty.ts", "const BUFFER_LIMIT = 1024 * 1024 * 2", "BUFFER_LIMIT");
        await fileSystem.File.WriteAllTextAsync(
            SnapshotPaths.CommittedReceipt,
            RefreshScenarioData.Serialize(CreateReceipt(CandidateDocument, normalizedDocumentPath: null) with
            {
                WatchedSources = [Observation(pinned.Path, pinned.Sha256, anchorMatched: true)],
            }));
        await WriteSourceWatchAsync(fileSystem, pinned);
        var runner = new ScriptedProcessRunner()
            .Expect("git", "rev-parse HEAD", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", $"show HEAD:{pinned.Path}", ScriptedProcessRunner.Ok("const BUFFER_LIMIT = 4 * 1024 * 1024"));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.VerifyAsync(CancellationToken.None);

        // A checkout that moved under the door disagrees with both readings: the committed pin
        // and the accepted receipt's own record of the same file.
        await Assert
            .That(outcome.Problems.Any(static problem =>
                problem.Contains("watched source 'packages/core/src/pty.ts' changed: pinned", StringComparison.Ordinal)))
            .IsTrue();
        await Assert
            .That(outcome.Problems.Any(static problem =>
                problem.Contains("the receipt records watched source 'packages/core/src/pty.ts' at", StringComparison.Ordinal)))
            .IsTrue();
    }

    /// <summary>
    /// Verify reproduces the receipt's watchedSources section rather than trusting it: here the
    /// checkout matches the committed pin exactly, and the only disagreement is the receipt's own
    /// anchorMatched record (ADR-0020).
    /// </summary>
    [Test]
    public async Task Verify_Should_Report_A_Receipt_Whose_Watched_Sources_It_Cannot_Reproduce()
    {
        var fileSystem = await CreateRepositoryAsync();
        await fileSystem.File.WriteAllBytesAsync(SnapshotPaths.AcceptedDocument, CandidateDocument);
        var pinned = RefreshScenarioData.Watched("packages/core/src/pty.ts", "const BUFFER_LIMIT = 1024 * 1024 * 2", "BUFFER_LIMIT");
        await fileSystem.File.WriteAllTextAsync(
            SnapshotPaths.CommittedReceipt,
            RefreshScenarioData.Serialize(CreateReceipt(CandidateDocument, normalizedDocumentPath: null) with
            {
                WatchedSources = [Observation(pinned.Path, pinned.Sha256, anchorMatched: false)],
            }));
        await WriteSourceWatchAsync(fileSystem, pinned);
        var runner = new ScriptedProcessRunner()
            .Expect("git", "rev-parse HEAD", ScriptedProcessRunner.Ok(RefreshScenarioData.Commit + "\n"))
            .Expect("git", $"show HEAD:{pinned.Path}", ScriptedProcessRunner.Ok("const BUFFER_LIMIT = 1024 * 1024 * 2"));
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        var outcome = await synchronizer.VerifyAsync(CancellationToken.None);

        await Assert.That(outcome.Problems.Single()).Contains("anchorMatched=false, but this checkout observes true");
    }

    [Test]
    public async Task Apply_Should_Repin_The_Watch_Over_The_Reviewed_Receipt()
    {
        var fileSystem = await CreateRepositoryAsync();
        var pinned = RefreshScenarioData.Watched("packages/core/src/pty.ts", "const BUFFER_LIMIT = 1024 * 1024 * 2", "BUFFER_LIMIT");
        await WriteSourceWatchAsync(fileSystem, pinned);
        var moved = RefreshScenarioData.Watched(pinned.Path, "const BUFFER_LIMIT = 1024 * 1024 * 4", "BUFFER_LIMIT");
        var receipt = CreateReceipt(CandidateDocument, "scratch/openapi.json") with
        {
            WatchedSources = [Observation(moved.Path, moved.Sha256, anchorMatched: true)],
        };
        _ = fileSystem.Directory.CreateDirectory("scratch");
        await fileSystem.File.WriteAllBytesAsync("scratch/openapi.json", CandidateDocument);
        await fileSystem.File.WriteAllTextAsync("scratch/receipt.json", RefreshScenarioData.Serialize(receipt));
        var runner = new ScriptedProcessRunner()
            .Expect("git", "fetch origin")
            .Expect("git", "rev-parse --verify")
            .Expect("git", "checkout --detach");
        var synchronizer = CreateSynchronizer(fileSystem, runner);

        _ = await synchronizer.ApplyAsync("scratch/receipt.json", CancellationToken.None);

        var repinned = await new SourceWatchLoader(fileSystem).LoadAsync(CancellationToken.None);
        await Assert.That(repinned.Sources.Single().Sha256).IsEqualTo(moved.Sha256);
        await Assert.That(repinned.Sources.Single().Behavior).IsEqualTo(pinned.Behavior);
    }

    [Test]
    public async Task Apply_Should_Refuse_A_Receipt_Whose_Anchor_Was_Lost()
    {
        var fileSystem = await CreateRepositoryAsync();
        var pinned = RefreshScenarioData.Watched("packages/core/src/pty.ts", "const BUFFER_LIMIT = 1024 * 1024 * 2", "BUFFER_LIMIT");
        await WriteSourceWatchAsync(fileSystem, pinned);
        var receipt = CreateReceipt(CandidateDocument, "scratch/openapi.json") with
        {
            WatchedSources = [Observation(pinned.Path, new string('d', 64), anchorMatched: false)],
        };
        _ = fileSystem.Directory.CreateDirectory("scratch");
        await fileSystem.File.WriteAllBytesAsync("scratch/openapi.json", CandidateDocument);
        await fileSystem.File.WriteAllTextAsync("scratch/receipt.json", RefreshScenarioData.Serialize(receipt));
        var synchronizer = CreateSynchronizer(fileSystem, new ScriptedProcessRunner());

        var exception = await Assert
            .That(async () => _ = await synchronizer.ApplyAsync("scratch/receipt.json", CancellationToken.None))
            .Throws<SnapshotRefreshException>();

        await Assert.That(exception!.Message).Contains("lost anchors in packages/core/src/pty.ts");
    }

    private static ReceiptWatchedSource Observation(string path, string sha256, bool anchorMatched) =>
        new()
        {
            Path = path,
            Sha256 = sha256,
            AnchorMatched = anchorMatched,
        };

    private static async Task<MockFileSystem> CreateRepositoryAsync()
    {
        var fileSystem = new MockFileSystem();
        _ = fileSystem.Directory.CreateDirectory("spec");
        await fileSystem.File.WriteAllBytesAsync(SnapshotPaths.AcceptedDocument, AcceptedDocument);
        await fileSystem.File.WriteAllTextAsync(SnapshotPaths.SnapshotMarkdown, RefreshScenarioData.SnapshotMarkdown);
        _ = fileSystem.Directory.CreateDirectory(SnapshotPaths.Submodule);
        return fileSystem;
    }

    private static async Task WritePatchSetAsync(MockFileSystem fileSystem)
    {
        const string patchContent = "diff --git a/x b/x";
        _ = fileSystem.Directory.CreateDirectory(SnapshotPaths.PatchesRoot);
        await fileSystem.File.WriteAllTextAsync(
            fileSystem.Path.Combine(SnapshotPaths.PatchesRoot, "001-test.json"),
            RefreshScenarioData.Serialize(
                RefreshScenarioData.Manifest(sha256: DocumentInspector.Sha256Hex(Encoding.UTF8.GetBytes(patchContent)))));
        await fileSystem.File.WriteAllTextAsync(fileSystem.Path.Combine(SnapshotPaths.PatchesRoot, "001-test.patch"), patchContent);
    }

    private static SnapshotSynchronizer CreateSynchronizer(MockFileSystem fileSystem, ScriptedProcessRunner runner) =>
        new(fileSystem, runner, new PatchSetLoader(fileSystem), new SourceWatchLoader(fileSystem),
            new WatchedSourceReader(fileSystem, runner));

    private static async Task WriteSourceWatchAsync(MockFileSystem fileSystem, params WatchedSource[] sources) =>
        await fileSystem.File.WriteAllTextAsync(
            SnapshotPaths.SourceWatch, RefreshScenarioData.Serialize(RefreshScenarioData.Watch(1, sources)));

    private static SnapshotReceipt CreateReceipt(byte[] normalizedBytes, string? normalizedDocumentPath)
    {
        var stats = DocumentInspector.Inspect(normalizedBytes);
        return new SnapshotReceipt
        {
            SchemaVersion = 1,
            UpstreamCommit = RefreshScenarioData.Commit,
            RawDocumentSha256 = DocumentInspector.Sha256Hex(normalizedBytes),
            GeneratedBaselineSha256 = null,
            Patches = [],
            NormalizedDocumentSha256 = DocumentInspector.Sha256Hex(normalizedBytes),
            NormalizedDocumentPath = normalizedDocumentPath,
            OperationSetDigest = stats.OperationSetDigest,
            OperationCount = stats.OperationIds.Count,
            AddedOperations = [],
            RemovedOperations = [],
            ComponentCount = stats.ComponentCount,
            ContentSchemaCount = stats.ContentSchemaCount,
        };
    }
}
