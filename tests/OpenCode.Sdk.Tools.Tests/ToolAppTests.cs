using Spectre.Console.Cli.Testing;

namespace OpenCode.Sdk.Tools.Tests;

public sealed class ToolAppTests
{
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
