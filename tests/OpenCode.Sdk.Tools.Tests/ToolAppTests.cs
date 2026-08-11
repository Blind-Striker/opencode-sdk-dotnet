using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCode.Sdk.Tools.Commands;
using OpenCode.Sdk.Tools.Infrastructure;
using OpenCode.Sdk.Tools.Infrastructure.Logging;
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
        var interceptors = provider.GetServices<ICommandInterceptor>().ToArray();

        await Assert.That(fileSystem).IsTypeOf<RealFileSystem>();
        await Assert.That(console).IsSameReferenceAs(AnsiConsole.Console);
        await Assert.That(logger).IsNotNull();
        await Assert.That(loggingOptions).IsNotNull();
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
        var fileSystem = new MockFileSystem();
        using var registrar = ToolApp.CreateRegistrar(services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IAnsiConsole, TestConsole>();
        });
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(["generate", "--log-level", "debug", "--log-file", logPath]);

        await Assert.That(result.ExitCode).IsEqualTo(1);
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
    public async Task RunAsync_Should_Fail_Loud_When_Generate_Is_Invoked()
    {
        using var registrar = ToolApp.CreateRegistrar();
        var tester = new CommandAppTester(registrar);
        tester.Configure(ToolApp.Configure);

        var result = await tester.RunAsync(["generate"]);

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("not implemented");
    }
}
