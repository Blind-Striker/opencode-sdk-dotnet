using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using OpenCode.Sdk.Tools.Generator.Emission;
using Testably.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class GeneratedSourceCompiler
{
    private static readonly CSharpParseOptions ParseOptions = new(
        LanguageVersion.CSharp14,
        preprocessorSymbols: ["NET"]);
    private static readonly PortableExecutableReference[] References = CreateReferences();

    /// <summary>
    /// The built-in Roslyn generators an MSBuild compilation of the SDK gets from the targeting
    /// pack and this hand-rolled compilation must register itself: System.Text.Json's, for the
    /// generated serializer contexts, and the LibraryImport generator, for the one P/Invoke stub
    /// the background-service door's process control declares (ADR-0026).
    /// </summary>
    private static readonly AnalyzerFileReference[] SourceGenerators = CreateSourceGeneratorReferences();

    /// <summary>
    /// Hand-written sources sitting <em>above</em> generated output instead of under it: both
    /// PTY families' public doors delegate to generated raw clients (ADR-0021), and the
    /// persistent family's session carries the generated <c>PersistentPtyInfo</c> the server's
    /// attach frame hands it, so unlike the behavior core they cannot compile against a plan that
    /// never emitted those twins. Each rides along only when the plan under test emitted what it
    /// consumes — a consumer listed against several emissions is skipped when any one is missing.
    /// Today's pinned plan emits them all, and a dedicated test asserts it keeps doing so
    /// (<c>SourceEmitterTests.Emit_Should_Produce_Every_GeneratedSurfaceConsumers_RequiredEmission</c>),
    /// so a renamed or dropped twin fails that assertion loudly instead of silently vanishing
    /// from this probe's coverage. A synthetic emitter fixture is free to omit a twin, in which
    /// case its consumers are skipped here rather than failing to compile. The background-service
    /// info probe has its own decoder and depends on no generated model; the stop door's
    /// persistent-terminal shutdown rides the generated <c>persistentPty.shutdown</c> door, so its
    /// shipped implementation and <c>OpenCodeServer</c>, which composes it, ride along with the
    /// persistent family's raw twin. The handoff sidecar requests its ticket raw through the
    /// pipeline on the persistent family's route, which the route table carries only when that
    /// family is emitted, so it rides the same raw twin.
    /// </summary>
    internal static readonly (string Consumer, string RequiredEmission)[] GeneratedSurfaceConsumers =
    [
        ("Internal/BackgroundService/ServicePtyHandoff.cs", "PersistentPtys/PersistentPtysRawClient.cs"),
        ("Internal/BackgroundService/ServicePtyShutdown.cs", "PersistentPtys/PersistentPtysRawClient.cs"),
        ("Internal/PersistentPtyFrameDecoder.cs", "Models/PersistentPtyInfo.cs"),
        ("OpenCodeServer.cs", "PersistentPtys/PersistentPtysRawClient.cs"),
        ("PersistentPtys/PersistentPtyAttachedFrame.cs", "Models/PersistentPtyInfo.cs"),
        ("PersistentPtys/PersistentPtyAttachment.cs", "Models/PersistentPtyInfo.cs"),
        ("PersistentPtys/PersistentPtyClient.cs", "Models/PersistentPtyInfo.cs"),
        ("PersistentPtys/PersistentPtyClient.cs", "PersistentPtys/PersistentPtyRawClient.cs"),
        ("PersistentPtys/PersistentPtySession.cs", "Models/PersistentPtyInfo.cs"),
        ("PersistentPtys/PersistentPtysClient.cs", "Models/PersistentPtyInfo.cs"),
        ("PersistentPtys/PersistentPtysClient.cs", "PersistentPtys/PersistentPtysRawClient.cs"),
        ("Ptys/PtyClient.cs", "Ptys/PtyRawClient.cs"),
        ("Ptys/PtysClient.cs", "Ptys/PtysRawClient.cs"),
    ];

    /// <summary>
    /// Compiles the generated sources together with the hand-written SDK sources so generated
    /// clients resolve the behavior core; an emitted path shadows its committed twin.
    /// </summary>
    /// <param name="sources">The freshly emitted sources.</param>
    /// <returns>Warning-or-worse diagnostics.</returns>
    public static async Task<Diagnostic[]> CompileWithSdkCoreAsync(IReadOnlyList<GeneratedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return Compile(sources, await LoadSdkCoreTreesAsync(sources)).Diagnostics;
    }

    public static async Task<Assembly> CompileAndLoadWithSdkCoreAsync(IReadOnlyList<GeneratedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var result = Compile(sources, await LoadSdkCoreTreesAsync(sources));
        if (result.Diagnostics.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                emitted.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    private static async Task<List<SyntaxTree>> LoadSdkCoreTreesAsync(IReadOnlyList<GeneratedSource> sources)
    {
        // The SDK project compiles with implicit usings; the probe replays the same set.
        const string implicitUsings = """
                                      global using System;
                                      global using System.Collections.Generic;
                                      global using System.IO;
                                      global using System.Linq;
                                      global using System.Net.Http;
                                      global using System.Threading;
                                      global using System.Threading.Tasks;
                                      """;
        var fileSystem = new RealFileSystem();
        var root = fileSystem.Path.Combine(AppContext.BaseDirectory, "Fixtures", "SdkSource");
        var emitted = sources.Select(static source => source.RelativePath).ToHashSet(StringComparer.Ordinal);
        var skipped = GeneratedSurfaceConsumers
            .Where(consumer => !emitted.Contains(consumer.RequiredEmission))
            .Select(static consumer => consumer.Consumer)
            .ToHashSet(StringComparer.Ordinal);
        var coreTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(implicitUsings, ParseOptions, "sdk:ImplicitUsings.cs", Encoding.UTF8),
        };
        foreach (var entry in fileSystem
                     .Directory
                     .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Select(path => (Path: path, Relative: fileSystem.Path.GetRelativePath(root, path).Replace('\\', '/')))
                     .OrderBy(static entry => entry.Relative, StringComparer.Ordinal))
        {
            if (skipped.Contains(entry.Relative))
            {
                continue;
            }

            var text = await fileSystem.File.ReadAllTextAsync(entry.Path, CancellationToken.None);
            if (!text.StartsWith("// Generated by OpenCode.Sdk.Tools", StringComparison.Ordinal))
            {
                coreTrees.Add(CSharpSyntaxTree.ParseText(text, ParseOptions, $"sdk:{entry.Relative}", Encoding.UTF8));
            }
        }

        return coreTrees;
    }

    private static CompilationResult Compile(IReadOnlyList<GeneratedSource> sources, IReadOnlyList<SyntaxTree> extraTrees)
    {
        var syntaxTrees = sources
            .Select(static source => CSharpSyntaxTree.ParseText(
                Encoding.UTF8.GetString(source.Utf8Source.Span),
                ParseOptions,
                source.RelativePath,
                Encoding.UTF8))
            .Concat(extraTrees);
        var compilation = CSharpCompilation.Create(
            $"GeneratedSourceProbe_{Guid.NewGuid():N}",
            syntaxTrees,
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true,
                // The SDK project's AllowUnsafeBlocks: the LibraryImport generator emits its stub as unsafe code.
                allowUnsafe: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            SourceGenerators.SelectMany(static reference => reference.GetGenerators(LanguageNames.CSharp)),
            parseOptions: ParseOptions);
        _ = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Diagnostic[] diagnostics =
        [
            .. generatorDiagnostics
                .Concat(outputCompilation.GetDiagnostics())
                .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error),
        ];
        return new CompilationResult(outputCompilation, diagnostics);
    }

    private static PortableExecutableReference[] CreateReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                ?? throw new InvalidOperationException("The runtime did not expose trusted platform assemblies.");
        return
        [
            .. trustedAssemblies
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(static path => MetadataReference.CreateFromFile(path)),
        ];
    }

    private static AnalyzerFileReference[] CreateSourceGeneratorReferences()
    {
        var fileSystem = new RealFileSystem();
        var frameworkDirectory = fileSystem.Path.GetDirectoryName(typeof(JsonSerializer).Assembly.Location)
                                 ?? throw new InvalidOperationException("The runtime framework directory could not be resolved.");
        var frameworkVersion = fileSystem.Path.GetFileName(frameworkDirectory);
        var dotnetRoot = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(frameworkDirectory, "..", "..", ".."));
        var analyzerDirectory = fileSystem.Path.Combine(
            dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", frameworkVersion, "analyzers", "dotnet", "cs");

        // The LibraryImport generator's own dependency sits beside it and is not an analyzer
        // reference of its own; loading it into the default context first is what lets the
        // generator's assembly resolve it, because the probe's loader resolves nothing itself.
        _ = AssemblyLoadContext.Default.LoadFromAssemblyPath(
            GeneratorPath(fileSystem, analyzerDirectory, "Microsoft.Interop.SourceGeneration.dll"));

        return
        [
            new AnalyzerFileReference(
                GeneratorPath(fileSystem, analyzerDirectory, "System.Text.Json.SourceGeneration.dll"),
                new CompilerAnalyzerAssemblyLoader()),
            new AnalyzerFileReference(
                GeneratorPath(fileSystem, analyzerDirectory, "Microsoft.Interop.LibraryImportGenerator.dll"),
                new CompilerAnalyzerAssemblyLoader()),
        ];
    }

    private static string GeneratorPath(RealFileSystem fileSystem, string analyzerDirectory, string fileName)
    {
        var path = fileSystem.Path.Combine(analyzerDirectory, fileName);
        if (!fileSystem.File.Exists(path))
        {
            throw new InvalidOperationException($"The source generator '{fileName}' was not found at '{path}'.");
        }

        return path;
    }

    private sealed record CompilationResult(Compilation Compilation, Diagnostic[] Diagnostics);
}
