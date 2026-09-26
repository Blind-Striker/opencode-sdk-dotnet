using OpenCode.Sdk.Tools.Output;

namespace OpenCode.Sdk.Tools.Tests.Output;

/// <summary>
/// <see cref="CliWrapProjectFormatter.ResponseFileContent"/> and
/// <see cref="CliWrapProjectFormatter.SdkRoot"/> are the pure parts behind the formatter's one
/// <c>dotnet-format</c> run; the real process launch is exercised by running <c>generate</c>
/// itself, never faked here.
/// </summary>
public sealed class CliWrapProjectFormatterTests
{
    [Test]
    public async Task ResponseFileContent_Should_Open_With_The_Include_Option()
    {
        var content = CliWrapProjectFormatter.ResponseFileContent(["Models/A.cs"]);

        await Assert.That(content).StartsWith("--include\n");
    }

    [Test]
    public async Task ResponseFileContent_Should_Quote_Each_Path_On_Its_Own_Line_In_Input_Order()
    {
        string[] paths = ["Models/B.cs", "Models/A.cs", "Sessions/Session Client.cs"];

        var content = CliWrapProjectFormatter.ResponseFileContent(paths);

        await Assert.That(content).IsEqualTo("--include\n\"Models/B.cs\"\n\"Models/A.cs\"\n\"Sessions/Session Client.cs\"\n");
    }

    [Test]
    public async Task ResponseFileContent_Should_Refuse_A_Path_With_A_Double_Quote()
    {
        await Assert.That(() => CliWrapProjectFormatter.ResponseFileContent(["Models/\"A\".cs"]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SdkRoot_Should_Return_The_Install_Root_Of_The_Selected_Version()
    {
        const string listing = "9.0.318 [C:\\Program Files\\dotnet\\sdk]\r\n10.0.303 [C:\\Program Files\\dotnet\\sdk]\r\n";

        var root = CliWrapProjectFormatter.SdkRoot(listing, "10.0.303");

        await Assert.That(root).IsEqualTo("C:\\Program Files\\dotnet\\sdk");
    }

    [Test]
    public async Task SdkRoot_Should_Not_Match_A_Version_That_Only_Shares_A_Prefix()
    {
        const string listing = "10.0.3030 [/opt/other/sdk]\n10.0.303 [/usr/share/dotnet/sdk]\n";

        var root = CliWrapProjectFormatter.SdkRoot(listing, "10.0.303");

        await Assert.That(root).IsEqualTo("/usr/share/dotnet/sdk");
    }

    [Test]
    public async Task SdkRoot_Should_Refuse_A_Listing_Without_The_Selected_Version()
    {
        await Assert.That(() => CliWrapProjectFormatter.SdkRoot("9.0.318 [/usr/share/dotnet/sdk]\n", "10.0.303"))
            .Throws<InvalidOperationException>();
    }
}
