using Microsoft.Extensions.DependencyInjection;
using OpenCode.Sdk.Tools.Commands;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

namespace OpenCode.Sdk.Tools;

/// <summary>Composition root for the repo tool: DI registrar plus the command surface.</summary>
public static class ToolApp
{
    /// <summary>Builds the DI registrar; tests inject service overrides.</summary>
    public static DependencyInjectionRegistrar CreateRegistrar(Action<IServiceCollection>? overrideServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(AnsiConsole.Console);
        overrideServices?.Invoke(services);
        return new DependencyInjectionRegistrar(services);
    }

    /// <summary>Registers the command surface; shared by the app and <c>CommandAppTester</c>.</summary>
    public static void Configure(IConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);

        configurator
            .SetApplicationName("opencode-tool")
            .AddCommand<GenerateCommand>("generate")
            .WithDescription("Regenerate the SDK model layer from spec/openapi.json.");
    }

    /// <summary>Entry point used by tools/opencode-tool.cs.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var registrar = CreateRegistrar();
        var app = new CommandApp(registrar);
        app.Configure(Configure);
        return await app.RunAsync(args).ConfigureAwait(false);
    }
}
