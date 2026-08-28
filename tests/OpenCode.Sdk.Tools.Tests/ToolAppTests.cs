using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCode.Sdk.Tools.Commands;
using OpenCode.Sdk.Tools.Generator;
using OpenCode.Sdk.Tools.Infrastructure;
using OpenCode.Sdk.Tools.Infrastructure.Logging;
using OpenCode.Sdk.Tools.Output.Abstractions;
using OpenCode.Sdk.Tools.Tests.Support;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;
using Testably.Abstractions;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class ToolAppTests
{
    [Test]
    public async Task CreateServices_Should_Resolve_Full_Hosting_Composition()
    {
        await using var provider = ToolApp.CreateServices().BuildServiceProvider();

        var fileSystem = provider.GetRequiredService<IFileSystem>();
        var console = provider.GetRequiredService<IAnsiConsole>();
        var logger = provider.GetRequiredService<ILogger<GenerateCommand>>();
        var loggingOptions = provider.GetRequiredService<ToolLoggingOptions>();
        var coordinator = provider.GetRequiredService<GenerationCoordinator>();
        var writer = provider.GetRequiredService<IGenerationWriter>();
        var formatter = provider.GetRequiredService<IProjectFormatter>();
        var interceptors = provider.GetServices<ICommandInterceptor>().ToArray();

        await Assert.That(fileSystem).IsTypeOf<RealFileSystem>();
        await Assert.That(console).IsSameReferenceAs(AnsiConsole.Console);
        await Assert.That(logger).IsNotNull();
        await Assert.That(loggingOptions).IsNotNull();
        await Assert.That(coordinator).IsNotNull();
        await Assert.That(writer).IsNotNull();
        await Assert.That(formatter).IsNotNull();
        await Assert.That(interceptors.Length).IsEqualTo(1);
        await Assert.That(interceptors[0]).IsTypeOf<GlobalOptionsInterceptor>();
    }

    [Test]
    public async Task CreateRegistrar_Should_Apply_Seam_Overrides_After_Production_Registrations()
    {
        var expected = new MockFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services => services.AddSingleton<IFileSystem>(expected));
        var resolver = registrar.Build();

        var actual = resolver.Resolve(typeof(IFileSystem));

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task RunAsync_Should_Apply_Global_Logging_Options_Before_Command_Execution()
    {
        const string logPath = "logs/tool.log";
        var fileSystem = GenerationTestData.CreateCommandFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
            services.AddSingleton<IProjectFormatter>(new RecordingProjectFormatter(fileSystem));
        });
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(["generate", "--log-level", "debug", "--log-file", logPath]);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(logPath)).IsTrue();
        var log = await fileSystem.File.ReadAllTextAsync(logPath, CancellationToken.None);
        await Assert.That(log).Contains("Generate command invoked.");
    }

    [Test]
    public async Task RunAsync_Should_Return_Nonzero_When_Command_Is_Unknown()
    {
        using var registrar = ToolApp.CreateRegistrar();
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(["does-not-exist"]);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
    }

    [Test]
    public async Task RunAsync_Should_Generate_Selected_Output()
    {
        var fileSystem = GenerationTestData.CreateCommandFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
            services.AddSingleton<IProjectFormatter>(new RecordingProjectFormatter(fileSystem));
        });
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(["generate"]);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(fileSystem.File.Exists(GenerationTestData.ManifestPath)).IsTrue();
        await Assert.That(fileSystem.File.Exists(GenerationTestData.MarkerPath)).IsTrue();
        await Assert.That(result.Output).Contains("Pending operations");
    }

    [Test]
    public async Task RunAsync_Should_Mark_Pending_Operations_With_Their_Bindability()
    {
        var fileSystem = GenerationTestData.CreateCommandFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
            services.AddSingleton<IProjectFormatter>(new RecordingProjectFormatter(fileSystem));
        });
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(["generate"]);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var marker = await fileSystem.File.ReadAllTextAsync(GenerationTestData.MarkerPath, CancellationToken.None);
        await Assert.That(marker).Contains("- v2.plugin.list [bindable]");
        await Assert.That(marker).Contains("- v2.session.list [refused: the success response must carry a JSON schema]");
    }

    [Test]
    public async Task RunAsync_Should_Return_Zero_When_Verify_Is_Clean()
    {
        var fileSystem = GenerationTestData.CreateCommandFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
            services.AddSingleton<IProjectFormatter>(new RecordingProjectFormatter(fileSystem));
        });
        var generateTester = new CommandAppTester(registrar);
        generateTester.Configure(ToolApp.Configure);
        _ = await generateTester.RunAsync(["generate"]);

        var verifyTester = new CommandAppTester(registrar);
        verifyTester.Configure(ToolApp.Configure);
        var result = await verifyTester.RunAsync(["generate", "--verify"]);

        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task RunAsync_Should_Return_Nonzero_And_Report_Verify_Drift()
    {
        const string generatedPath = "src/OpenCode.Sdk/Internal/Serialization/OpenCodeJsonContext.cs";
        var fileSystem = GenerationTestData.CreateCommandFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
            services.AddSingleton<IProjectFormatter>(new RecordingProjectFormatter(fileSystem));
        });
        var generateTester = new CommandAppTester(registrar);
        generateTester.Configure(ToolApp.Configure);
        _ = await generateTester.RunAsync(["generate"]);
        await fileSystem.File.WriteAllTextAsync(generatedPath, GenerationTestData.OwnedContent("drift"), CancellationToken.None);

        var verifyTester = new CommandAppTester(registrar);
        verifyTester.Configure(ToolApp.Configure);
        var result = await verifyTester.RunAsync(["generate", "--verify"]);

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("Internal/Serialization/OpenCodeJsonContext.cs");
    }
}
