using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests;

public sealed class SystemEnvironmentProviderTests
{
    [Test]
    public async Task GetEnvironmentVariable_Should_Read_The_Process_Environment()
    {
        const string name = "OPENCODE_SDK_TEST_VARIABLE";
        Environment.SetEnvironmentVariable(name, "expected");
        try
        {
            var value = new SystemEnvironmentProvider().GetEnvironmentVariable(name);

            await Assert.That(value).IsEqualTo("expected");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Test]
    public async Task GetEnvironmentVariable_Should_Return_Null_When_The_Variable_Is_Absent()
    {
        var value = new SystemEnvironmentProvider().GetEnvironmentVariable("OPENCODE_SDK_TEST_ABSENT_VARIABLE");

        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task GetEnvironmentVariable_Should_Refuse_A_Blank_Name()
    {
        _ = Assert.Throws<ArgumentException>(() => _ = new SystemEnvironmentProvider().GetEnvironmentVariable(" "));
    }
}
