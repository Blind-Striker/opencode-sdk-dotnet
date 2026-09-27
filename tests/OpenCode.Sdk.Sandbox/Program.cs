using OpenCode.Sdk.Sandbox;

var arguments = SandboxArguments.Parse(args);
if (arguments is null)
{
    await Console.Error.WriteLineAsync(SandboxArguments.Usage).ConfigureAwait(false);
    return 1;
}

return arguments.Mode is SandboxMode.Standalone
    ? await StandaloneServerWalkthrough.RunAsync().ConfigureAwait(false)
    : await SandboxRunner.RunAsync(arguments).ConfigureAwait(false);
