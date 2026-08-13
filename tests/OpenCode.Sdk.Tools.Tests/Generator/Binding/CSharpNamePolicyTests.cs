using OpenCode.Sdk.Tools.Generator.Binding;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class CSharpNamePolicyTests
{
    [Test]
    [Arguments("id", "Id")]
    [Arguments("sessionID", "SessionId")]
    [Arguments("messageID", "MessageId")]
    [Arguments("callID", "CallId")]
    [Arguments("URL", "Url")]
    [Arguments("APIError", "ApiError")]
    public async Task ToPascalCase_Should_Use_Ordinary_Acronym_Casing(string wireName, string expected)
    {
        var result = CSharpNamePolicy.ToPascalCase(wireName);

        await Assert.That(result).IsEqualTo(expected);
    }
}
