using System.Net;
using OpenCode.Sdk.Models;
using OpenCode.Sdk.Tests.Support;

namespace OpenCode.Sdk.Tests;

public sealed class FileSystemClientContractTests
{
    [Test]
    public async Task ListEntriesAsync_Should_Return_The_Typed_Entries_With_Their_Location()
    {
        const string entries = "[{\"path\":\"src\",\"type\":\"directory\"},{\"path\":\"README.md\",\"type\":\"file\"}]";
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope(entries));

        var response = await scenario.Client.FileSystem.ListEntriesAsync();

        await Assert.That(response.Entries.Count).IsEqualTo(2);
        await Assert.That(response.Entries[0].Path).IsEqualTo("src");
        await Assert.That(response.Entries[0].Type).IsEqualTo(FileSystemEntryType.Directory);
        await Assert.That(response.Entries[1].Path).IsEqualTo("README.md");
        await Assert.That(response.Entries[1].Type).IsEqualTo(FileSystemEntryType.File);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri)
            .IsEqualTo(new Uri("http://localhost:4096/api/fs/list"));
    }

    [Test]
    public async Task ListEntriesAsync_Should_Return_An_Empty_List_With_Its_Location()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope("[]"));

        var response = await scenario.Client.FileSystem.ListEntriesAsync();

        await Assert.That(response.Entries.Count).IsEqualTo(0);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
    }

    [Test]
    public async Task ListEntriesAsync_Should_Send_The_Path_And_Location_Query()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope("[]"));

        _ = await scenario.Client.FileSystem.ListEntriesAsync(new FsListRequest
        {
            Path = "src",
            Location = new LocationSelector { Workspace = "wrk_1" },
        });

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/fs/list?location[workspace]=wrk_1&path=src");
    }

    [Test]
    public async Task ListEntriesAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.FileSystem.ListEntriesAsync())
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task ListEntriesAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.FileSystem.ListEntriesAsync(requestOptions: OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }

    [Test]
    public async Task FindEntriesAsync_Should_Return_The_Typed_Entries_With_Their_Location()
    {
        using var scenario = ContractScenario.Responding(
            HttpStatusCode.OK, WireBodyData.LocationEnvelope($"[{WireBodyData.FileSystemEntry}]"));

        var response = await scenario.Client.FileSystem.FindEntriesAsync(new FsFindRequest { Query = "todo" });

        await Assert.That(response.Entries.Count).IsEqualTo(1);
        await Assert.That(response.Entries[0].Path).IsEqualTo("src/App.cs");
        await Assert.That(response.Entries[0].Type).IsEqualTo(FileSystemEntryType.File);
        await Assert.That(response.Location.Project.Id).IsEqualTo("prj_1");
        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/fs/find?query=todo");
    }

    [Test]
    public async Task FindEntriesAsync_Should_Send_The_Enum_Type_As_Its_Wire_Value()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.OK, WireBodyData.LocationEnvelope("[]"));

        _ = await scenario.Client.FileSystem.FindEntriesAsync(new FsFindRequest
        {
            Query = "todo",
            Type = FsFindRequestType.Directory,
            Limit = "10",
        });

        await Assert.That(scenario.Requests.Single().RequestUri!.AbsoluteUri)
            .IsEqualTo("http://localhost:4096/api/fs/find?query=todo&type=directory&limit=10");
    }

    [Test]
    public async Task FindEntriesAsync_Should_Throw_The_Declared_400_Error()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.BadRequest, WireBodyData.InvalidRequestError);

        var exception = await Assert
            .That(async () => _ = await scenario.Client.FileSystem.FindEntriesAsync(new FsFindRequest { Query = "todo" }))
            .Throws<OpenCodeApiException>();

        await Assert.That(exception!.Status).IsEqualTo(400);
        await Assert.That(exception.Error).IsTypeOf<InvalidRequestError>();
    }

    [Test]
    public async Task FindEntriesAsync_Should_Return_The_401_Error_On_The_NoThrow_Spine()
    {
        using var scenario = ContractScenario.Responding(HttpStatusCode.Unauthorized, WireBodyData.UnauthorizedError);

        var response = await scenario.Client.FileSystem.FindEntriesAsync(
            new FsFindRequest { Query = "todo" },
            OpenCodeRequestOptions.NoThrow);

        await Assert.That(response.IsError).IsTrue();
        await Assert.That(response.Status).IsEqualTo(401);
        await Assert.That(response.Error).IsTypeOf<UnauthorizedError>();
    }
}
