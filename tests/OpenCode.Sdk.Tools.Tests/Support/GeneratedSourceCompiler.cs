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
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);
    private static readonly PortableExecutableReference[] References = CreateReferences();
    private static readonly AnalyzerFileReference SourceGenerator = CreateSourceGeneratorReference();

    public static Diagnostic[] Compile(IReadOnlyList<GeneratedSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var syntaxTrees = sources.Select(static source => CSharpSyntaxTree.ParseText(
            Encoding.UTF8.GetString(source.Utf8Source.Span),
            ParseOptions,
            source.RelativePath,
            Encoding.UTF8));
        var compilation = CSharpCompilation.Create(
            "GeneratedSourceProbe",
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

        return
        [
            .. generatorDiagnostics.Concat(outputCompilation.GetDiagnostics())
                .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error),
        ];
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
}
