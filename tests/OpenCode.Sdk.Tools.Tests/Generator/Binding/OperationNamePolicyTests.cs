using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class OperationNamePolicyTests
{
    [Test]
    [Arguments("v2.health.get", "get", "GetHealthAsync")]
    [Arguments("v2.session.message", "get", "GetMessageAsync")]
    [Arguments("v2.session.get", "get", "GetSessionAsync")]
    [Arguments("v2.session.list", "get", "ListSessionsAsync")]
    [Arguments("v2.session.create", "post", "CreateSessionAsync")]
    [Arguments("v2.message.list", "get", "ListMessagesAsync")]
    [Arguments("v2.session.remove", "delete", "RemoveSessionAsync")]
    [Arguments("v2.session.rename", "post", "RenameSessionAsync")]
    [Arguments("v2.shell.list", "get", "ListShellsAsync")]
    [Arguments("v2.shell.timeout", "patch", "TimeoutShellAsync")]
    [Arguments("v2.shell.output", "get", "GetOutputAsync")]
    public async Task MethodName_Should_Lead_With_The_Structural_Verb(string operationId, string method, string expected)
    {
        await Assert.That(OperationNamePolicy.MethodName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    public async Task MethodName_Should_Keep_A_Mid_Position_Verb_Segment_In_The_Subject()
    {
        await Assert.That(OperationNamePolicy.MethodName(Operation("v2.cache.get.entry")))
            .IsEqualTo("GetGetEntryAsync");
    }

    [Test]
    public async Task MethodName_Should_Return_Null_When_The_Group_Cannot_Be_Pluralized()
    {
        await Assert.That(OperationNamePolicy.MethodName(Operation("v2.boss.list"))).IsNull();
    }

    [Test]
    [Arguments("v2.health.get", "get", "HealthResponse")]
    [Arguments("v2.session.message", "get", "SessionMessageResponse")]
    [Arguments("v2.session.get", "get", "SessionResponse")]
    [Arguments("v2.session.list", "get", "SessionListResponse")]
    [Arguments("v2.session.create", "post", "SessionCreateResponse")]
    [Arguments("v2.message.list", "get", "MessageListResponse")]
    [Arguments("v2.session.remove", "delete", "SessionRemoveResponse")]
    [Arguments("v2.shell.timeout", "patch", "ShellTimeoutResponse")]
    [Arguments("v2.shell.output", "get", "ShellOutputResponse")]
    [Arguments("v2.shell.get", "get", "ShellResponse")]
    public async Task ResponseTypeName_Should_Fold_Non_Get_Verbs(string operationId, string method, string expected)
    {
        await Assert.That(OperationNamePolicy.ResponseTypeName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("v2.health.get", "get", false, "Get")]
    [Arguments("v2.session.message", "get", true, "GetMessage")]
    [Arguments("v2.session.get", "get", true, "GetSession")]
    [Arguments("v2.session.list", "get", true, "ListSessions")]
    [Arguments("v2.session.create", "post", true, "CreateSession")]
    [Arguments("v2.message.list", "get", true, "ListMessages")]
    public async Task RouteMemberName_Should_Fall_Back_To_The_Group_On_Client_Placement_Only(string operationId,
        string method, bool clientPlacement, string expected)
    {
        var placement = clientPlacement ? GroupPlacement.Client : GroupPlacement.Root;

        await Assert.That(OperationNamePolicy.RouteMemberName(Operation(operationId, method), placement)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("v2.health.get", "get", "Health")]
    [Arguments("v2.session.message", "get", "Message")]
    [Arguments("v2.session.get", "get", "Session")]
    [Arguments("v2.session.list", "get", "Sessions")]
    [Arguments("v2.session.create", "post", "Session")]
    [Arguments("v2.message.list", "get", "Messages")]
    public async Task PayloadName_Should_Pluralize_The_Group_For_List_Operations(string operationId, string method,
        string expected)
    {
        await Assert.That(OperationNamePolicy.PayloadName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    public async Task PayloadName_Should_Return_Null_When_The_Group_Cannot_Be_Pluralized()
    {
        await Assert.That(OperationNamePolicy.PayloadName(Operation("v2.boss.list"))).IsNull();
    }

    [Test]
    [Arguments("v2.session.list", "get", "SessionListRequest")]
    [Arguments("v2.message.list", "get", "MessageListRequest")]
    [Arguments("v2.session.create", "post", "SessionCreateRequest")]
    [Arguments("v2.session.rename", "post", "SessionRenameRequest")]
    [Arguments("v2.shell.timeout", "patch", "ShellTimeoutRequest")]
    [Arguments("v2.shell.output", "get", "ShellOutputRequest")]
    [Arguments("v2.shell.get", "get", "ShellRequest")]
    public async Task RequestTypeName_Should_Compose_Group_Subject_And_Verb(string operationId, string method,
        string expected)
    {
        await Assert.That(OperationNamePolicy.RequestTypeName(Operation(operationId, method))).IsEqualTo(expected);
    }

    private static SpecOperation Operation(string operationId, string method = "get")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        return new SpecOperation
        {
            OperationId = operationId,
            Segments = [.. operationId.Split('.').Skip(1)],
            Method = method,
            Path = "/api/x",
            HasWildcardPath = false,
            IsWebSocket = false,
            IsSse = false,
            IsDeprecated = false,
            Parameters = [],
            Responses = [],
        };
    }
}
