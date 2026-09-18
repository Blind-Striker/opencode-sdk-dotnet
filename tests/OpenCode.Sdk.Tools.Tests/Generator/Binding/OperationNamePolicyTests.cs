using OpenCode.Sdk.Tools.Generator.Binding;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class OperationNamePolicyTests
{
    [Test]
    [Arguments("health.get", "get", "GetHealthAsync")]
    [Arguments("session.message", "get", "GetMessageAsync")]
    [Arguments("session.get", "get", "GetSessionAsync")]
    [Arguments("session.list", "get", "ListSessionsAsync")]
    [Arguments("session.create", "post", "CreateSessionAsync")]
    [Arguments("message.list", "get", "ListMessagesAsync")]
    [Arguments("session.remove", "delete", "RemoveSessionAsync")]
    [Arguments("session.rename", "post", "RenameSessionAsync")]
    [Arguments("shell.list", "get", "ListShellsAsync")]
    [Arguments("shell.timeout", "patch", "TimeoutShellAsync")]
    [Arguments("shell.output", "get", "GetOutputAsync")]
    public async Task MethodName_Should_Lead_With_The_Structural_Verb(string operationId, string method, string expected)
    {
        await Assert.That(OperationNamePolicy.MethodName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    public async Task MethodName_Should_Keep_A_Mid_Position_Verb_Segment_In_The_Subject()
    {
        await Assert.That(OperationNamePolicy.MethodName(Operation("cache.get.entry")))
            .IsEqualTo("GetGetEntryAsync");
    }

    [Test]
    public async Task MethodName_Should_Return_Null_When_The_Group_Cannot_Be_Pluralized()
    {
        await Assert.That(OperationNamePolicy.MethodName(Operation("boss.list"))).IsNull();
    }

    [Test]
    [Arguments("ListMessagesAsync", "EnumerateMessagesAsync")]
    [Arguments("ListItemsAsync", "EnumerateItemsAsync")]
    public async Task EnumerationMethodName_Should_Replace_The_List_Verb(string methodName, string expected)
    {
        await Assert.That(OperationNamePolicy.EnumerationMethodName(methodName)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("GetMessagesAsync")]
    [Arguments("ListMessages")]
    public async Task EnumerationMethodName_Should_Return_Null_Outside_Async_List_Methods(string methodName)
    {
        await Assert.That(OperationNamePolicy.EnumerationMethodName(methodName)).IsNull();
    }

    [Test]
    [Arguments("health.get", "get", "HealthResponse")]
    [Arguments("session.message", "get", "SessionMessageResponse")]
    [Arguments("session.get", "get", "SessionResponse")]
    [Arguments("session.list", "get", "SessionListResponse")]
    [Arguments("session.create", "post", "SessionCreateResponse")]
    [Arguments("message.list", "get", "MessageListResponse")]
    [Arguments("session.remove", "delete", "SessionRemoveResponse")]
    [Arguments("shell.timeout", "patch", "ShellTimeoutResponse")]
    [Arguments("shell.output", "get", "ShellOutputResponse")]
    [Arguments("shell.get", "get", "ShellResponse")]
    public async Task ResponseTypeName_Should_Fold_Non_Get_Verbs(string operationId, string method, string expected)
    {
        await Assert.That(OperationNamePolicy.ResponseTypeName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("health.get", "get", "HealthData")]
    [Arguments("session.message", "get", "SessionMessageData")]
    [Arguments("session.create", "post", "SessionCreateData")]
    [Arguments("shell.timeout", "patch", "ShellTimeoutData")]
    public async Task PayloadTypeName_Should_Replace_The_Response_Spine_Suffix(string operationId, string method,
        string expected)
    {
        await Assert.That(OperationNamePolicy.PayloadTypeName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("health.get", "get", false, "Get")]
    [Arguments("session.message", "get", true, "GetMessage")]
    [Arguments("session.get", "get", true, "GetSession")]
    [Arguments("session.list", "get", true, "ListSessions")]
    [Arguments("session.create", "post", true, "CreateSession")]
    [Arguments("message.list", "get", true, "ListMessages")]
    public async Task RouteMemberName_Should_Fall_Back_To_The_Group_On_Client_Placement_Only(string operationId,
        string method, bool clientPlacement, string expected)
    {
        var placement = clientPlacement ? GroupPlacement.Client : GroupPlacement.Root;

        await Assert.That(OperationNamePolicy.RouteMemberName(Operation(operationId, method), placement)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("health.get", "get", "Health")]
    [Arguments("session.message", "get", "Message")]
    [Arguments("session.get", "get", "Session")]
    [Arguments("session.list", "get", "Sessions")]
    [Arguments("session.create", "post", "Session")]
    [Arguments("message.list", "get", "Messages")]
    public async Task PayloadName_Should_Pluralize_The_Group_For_List_Operations(string operationId, string method,
        string expected)
    {
        await Assert.That(OperationNamePolicy.PayloadName(Operation(operationId, method))).IsEqualTo(expected);
    }

    [Test]
    public async Task PayloadName_Should_Return_Null_When_The_Group_Cannot_Be_Pluralized()
    {
        await Assert.That(OperationNamePolicy.PayloadName(Operation("boss.list"))).IsNull();
    }

    [Test]
    [Arguments("session.list", "get", "SessionListRequest")]
    [Arguments("message.list", "get", "MessageListRequest")]
    [Arguments("session.create", "post", "SessionCreateRequest")]
    [Arguments("session.rename", "post", "SessionRenameRequest")]
    [Arguments("shell.timeout", "patch", "ShellTimeoutRequest")]
    [Arguments("shell.output", "get", "ShellOutputRequest")]
    [Arguments("shell.get", "get", "ShellRequest")]
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
            Segments = [.. operationId.Split('.')],
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
