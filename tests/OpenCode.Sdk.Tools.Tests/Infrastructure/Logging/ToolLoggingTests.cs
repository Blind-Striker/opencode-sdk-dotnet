using Microsoft.Extensions.Logging;
using OpenCode.Sdk.Tools.Infrastructure.Logging;
using Spectre.Console.Testing;
using Testably.Abstractions.Testing;

namespace OpenCode.Sdk.Tools.Tests.Infrastructure.Logging;

public sealed class ToolLoggingTests
{
    [Test]
    [Arguments("trace", LogLevel.Trace)]
    [Arguments("debug", LogLevel.Debug)]
    [Arguments("info", LogLevel.Information)]
    [Arguments("warning", LogLevel.Warning)]
    [Arguments("error", LogLevel.Error)]
    [Arguments("none", LogLevel.None)]
    public async Task ConvertFrom_Should_Map_Tooling_Log_Level(string value, LogLevel expected)
    {
        var converter = new ToolLogLevelConverter();

        var actual = converter.ConvertFromInvariantString(value);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task Log_Should_Filter_Messages_Below_Default_Warning_Level()
    {
        using var console = new TestConsole();
        var options = new ToolLoggingOptions();
        using var provider = new SpectreConsoleLoggerProvider(console, options);
        var logger = provider.CreateLogger("OpenCode.Sdk.Tools.Tests");

        logger.Log(
            LogLevel.Information,
            new EventId(1),
            "Information message",
            null,
            static (message, _) => message);
        logger.Log(
            LogLevel.Warning,
            new EventId(2),
            "Warning message",
            null,
            static (message, _) => message);

        await Assert.That(console.Output).DoesNotContain("Information message");
        await Assert.That(console.Output).Contains("Warning message");
    }

    [Test]
    public async Task Log_Should_Render_Spectre_Markup_As_Literal_Text()
    {
        const string message = "[red]Consumer-provided text[/]";
        using var console = new TestConsole();
        var options = new ToolLoggingOptions();
        using var provider = new SpectreConsoleLoggerProvider(console, options);
        var logger = provider.CreateLogger("OpenCode.Sdk.Tools.Tests");

        logger.Log(
            LogLevel.Warning,
            new EventId(3),
            message,
            null,
            static (text, _) => text);

        await Assert.That(console.Output).Contains(message);
    }

    [Test]
    public async Task CreateLogger_Should_Fail_Loud_When_Configured_Log_File_Cannot_Be_Initialized()
    {
        // A file occupying the log directory's path makes initialization fail identically
        // on every OS; the Windows-only ACL simulation is not portable to the Linux/macOS legs.
        var fileSystem = new MockFileSystem();
        await fileSystem.File.WriteAllTextAsync("logs", string.Empty);
        var options = new ToolLoggingOptions();
        options.Apply(LogLevel.Debug, "logs/tool.log");

        await Assert
            .That(() =>
            {
                using var provider = new FileLoggerProvider(fileSystem, options);
                provider.CreateLogger("OpenCode.Sdk.Tools.Tests");
            })
            .Throws<IOException>();
    }
}
