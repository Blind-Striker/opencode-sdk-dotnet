using Spectre.Console;
using Spectre.Console.Cli;

namespace OpenCode.Sdk.Tools.Commands;

/// <summary>Fail-loud stub; the generator pipeline replaces the body in a later slice.</summary>
public sealed class GenerateCommand : AsyncCommand
{
    private readonly IAnsiConsole _console;

    /// <summary>Creates the command; the console is injected so tests capture output.</summary>
    public GenerateCommand(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
    }

    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _console.MarkupLine(
            "[red]generate is not implemented yet[/] — the generator pipeline has not landed.");
        return Task.FromResult(1);
    }
}
