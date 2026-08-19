using PublicApiGenerator;

namespace OpenCode.Sdk.Tests;

public sealed class PublicApiBaselineTests
{
    [Test]
    public async Task PublicApi_Should_Match_The_Reviewed_Baseline()
    {
        var api = typeof(OpenCodeClient).Assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false,
        });

        // Every target framework compiles the same public surface; one reviewed baseline
        // covers them all.
        var settings = new VerifySettings();
        settings.UseFileName("PublicApi");
        // Keep the reviewed baseline a normal LF-terminated text artifact on every OS.
        await Verify(string.Concat(api, "\n"), settings);
    }
}
