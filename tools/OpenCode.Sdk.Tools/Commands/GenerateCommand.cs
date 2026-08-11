using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace OpenCode.Sdk.Tools.Commands;

/// <summary>Fail-loud stub; the generator pipeline replaces the body in a later slice.</summary>
public sealed partial class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    private readonly IAnsiConsole _console;
    private readonly ILogger<GenerateCommand> _logger;

    /// <summary>Creates the command with its console and logging seams.</summary>
    public GenerateCommand(IAnsiConsole console, ILogger<GenerateCommand> logger)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(logger);

        _console = console;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        LogInvocation(_logger);
        _console.MarkupLine("[red]generate is not implemented yet[/] — the generator pipeline has not landed.");

        return Task.FromResult(1);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Generate command invoked.")]
    private static partial void LogInvocation(ILogger logger);

    /// <summary>Defines settings for the generate command.</summary>
    public sealed class Settings : GlobalSettings;
}
