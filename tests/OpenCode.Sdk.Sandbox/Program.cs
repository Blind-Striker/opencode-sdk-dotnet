using OpenCode.Sdk.Sandbox;

if (args.Contains("--standalone", StringComparer.Ordinal))
{
    return await StandaloneServerWalkthrough.RunAsync().ConfigureAwait(false);
}

return await SandboxRunner.RunAsync(args).ConfigureAwait(false);
