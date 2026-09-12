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

    /// <summary>
    /// A whole name that opens with a digit needs the underscore guard; a fragment appended to a
    /// stem does not, and taking it there would spell an interior underscore the repository's
    /// format pass rewrites out of the declaration without renaming the file it was written to.
    /// </summary>
    [Test]
    [Arguments("0", "_0", "0")]
    [Arguments("2xx", "_2Xx", "2Xx")]
    [Arguments("websearch", "Websearch", "Websearch")]
    public async Task ToPascalCaseFragment_Should_Leave_A_Leading_Digit_Unguarded(string wireName, string wholeName,
        string fragment)
    {
        await Assert.That(CSharpNamePolicy.ToPascalCase(wireName)).IsEqualTo(wholeName);
        await Assert.That(CSharpNamePolicy.ToPascalCaseFragment(wireName)).IsEqualTo(fragment);
    }

    [Test]
    [Arguments("class", false)]
    [Arguments("string", false)]
    [Arguments("record", true)]
    [Arguments("Session", true)]
    [Arguments("_reserved", true)]
    public async Task IsValidIdentifier_Should_Reject_Reserved_Keywords(string candidate, bool expected)
    {
        await Assert.That(CSharpNamePolicy.IsValidIdentifier(candidate)).IsEqualTo(expected);
    }
}
