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
    private static readonly AnalyzerFileReference SourceGenerator = CreateSourceGeneratorReference();

    /// <summary>
    /// Hand-written sources sitting <em>above</em> generated output instead of under it: both
    /// PTY families' public doors delegate to generated raw clients (ADR-0021), so unlike the
    /// behavior core they cannot compile against a plan that never emitted their twin. Each
    /// rides along only when the plan under test emitted the raw client it delegates to —
    /// today's pinned plan does, and a dedicated test asserts it keeps doing so
    /// (<c>SourceEmitterTests.Emit_Should_Produce_Every_GeneratedSurfaceConsumers_RequiredEmission</c>),
    /// so a renamed or dropped raw client fails that assertion loudly instead of silently
    /// vanishing from this probe's coverage. A synthetic emitter fixture is free to omit the
    /// twin, in which case its consumer is skipped here rather than failing to compile.
    /// </summary>
    internal static readonly (string Consumer, string RequiredEmission)[] GeneratedSurfaceConsumers =
    [
        ("PersistentPtys/PersistentPtyClient.cs", "PersistentPtys/PersistentPtyRawClient.cs"),
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
                deterministic: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            SourceGenerator.GetGenerators(LanguageNames.CSharp),
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

    private static AnalyzerFileReference CreateSourceGeneratorReference()
    {
        var fileSystem = new RealFileSystem();
        var frameworkDirectory = fileSystem.Path.GetDirectoryName(typeof(JsonSerializer).Assembly.Location)
                                 ?? throw new InvalidOperationException("The runtime framework directory could not be resolved.");
        var frameworkVersion = fileSystem.Path.GetFileName(frameworkDirectory);
        var dotnetRoot = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(frameworkDirectory, "..", "..", ".."));
        var generatorPath = fileSystem.Path.Combine(
            dotnetRoot,
            "packs",
            "Microsoft.NETCore.App.Ref",
            frameworkVersion,
            "analyzers",
            "dotnet",
            "cs",
            "System.Text.Json.SourceGeneration.dll");
        if (!fileSystem.File.Exists(generatorPath))
        {
            throw new InvalidOperationException($"The System.Text.Json source generator was not found at '{generatorPath}'.");
        }

        return new AnalyzerFileReference(generatorPath, new CompilerAnalyzerAssemblyLoader());
    }

    private sealed record CompilationResult(Compilation Compilation, Diagnostic[] Diagnostics);
}
