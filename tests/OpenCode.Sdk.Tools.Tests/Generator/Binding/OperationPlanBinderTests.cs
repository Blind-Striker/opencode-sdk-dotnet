using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;
using OpenCode.Sdk.Tools.Tests.Support;
using static OpenCode.Sdk.Tools.Tests.Support.BindingScenarioData;

namespace OpenCode.Sdk.Tools.Tests.Generator.Binding;

public sealed class OperationPlanBinderTests
{
    [Test]
    public async Task Bind_Should_Create_The_Selected_Pinned_Client_Plans()
    {
        var plan = await new BindingTestHost().BindPinnedAsync();

        await Assert.That(plan.Clients.Select(static client => client.Name)
            .SequenceEqual(
                ["OpenCodeClient", "SessionClient", "SessionsClient", "ShellClient", "ShellsClient"],
                StringComparer.Ordinal)).IsTrue();
        await Assert.That(plan.Clients.All(static client => client.Namespace == "OpenCode.Sdk")).IsTrue();

        var root = plan.Clients.Single(static client => client.Role == ClientRole.Root);
        await Assert.That(root.Name).IsEqualTo("OpenCodeClient");
        await Assert.That(root.SubClients.Select(static subClient => subClient.PropertyName)
            .SequenceEqual(["Sessions", "Shells"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(root.SubClients.Select(static subClient => subClient.TypeName)
            .SequenceEqual(["SessionsClient", "ShellsClient"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(root.HandleFactory).IsNull();
        await Assert.That(root.HandleParameter).IsNull();

        var health = root.Operations.Single();
        await Assert.That(health.MethodName).IsEqualTo("GetHealthAsync");
        await Assert.That(health.HttpMethod).IsEqualTo("get");
        await Assert.That(health.RouteTemplate).IsEqualTo("/api/health");
        await Assert.That(health.RouteContainerName).IsEqualTo("Health");
        await Assert.That(health.RouteMemberName).IsEqualTo("Get");
        await Assert.That(health.Parameters).IsEmpty();
        await Assert.That(health.Summary).IsEqualTo("Check server health");
        await Assert.That(health.Description).IsNotNull();
        await Assert.That(health.Envelope!.ResponseTypeName).IsEqualTo("HealthResponse");
        await Assert.That(health.Envelope.AdapterTypeName).IsEqualTo("HealthResponseAdapter");
        await Assert.That(health.Envelope.PayloadName).IsEqualTo("Health");
        await Assert.That(health.Envelope.PayloadTypeName).IsEqualTo("ServiceHealth");
        await Assert.That(health.Envelope.Kind).IsEqualTo(EnvelopeKind.Bare);
        await Assert.That(health.ErrorMap.Statuses.Select(static status => status.StatusCode)
            .SequenceEqual([400, 401])).IsTrue();
        await Assert.That(health.ErrorMap.Statuses[0].Tags.Single().Tag).IsEqualTo("InvalidRequestError");
        await Assert.That(health.ErrorMap.Statuses[0].Tags.Single().TypeName).IsEqualTo("InvalidRequestError");
        await Assert.That(health.ErrorMap.Statuses[1].Tags.Single().Tag).IsEqualTo("UnauthorizedError");

        await Assert.That(root.ContainerName).IsNull();

        var sessions = plan.Clients.Single(static client => client.Name == "SessionsClient");
        await Assert.That(sessions.ContainerName).IsEqualTo("Sessions");
        await Assert.That(sessions.Operations.Select(static operation => operation.MethodName)
            .SequenceEqual(["CreateSessionAsync", "ListSessionsAsync"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(sessions.HandleFactory!.MethodName).IsEqualTo("GetSessionClient");
        await Assert.That(sessions.HandleFactory.HandleTypeName).IsEqualTo("SessionClient");
        await Assert.That(sessions.HandleFactory.Parameter.WireName).IsEqualTo("sessionID");
        await Assert.That(sessions.HandleFactory.Parameter.Name).IsEqualTo("sessionId");
        await Assert.That(sessions.HandleFactory.Parameter.TypeName).IsEqualTo("string");

        var list = sessions.Operations.Single(static operation => operation.MethodName == "ListSessionsAsync");
        await Assert.That(list.QueryRequest!.TypeName).IsEqualTo("SessionListRequest");
        await Assert.That(list.QueryRequest.DerivesFromListRequest).IsFalse();
        await Assert.That(list.QueryRequest.Properties.Select(static property => property.PropertyName)
            .SequenceEqual(
                ["Workspace", "Limit", "Order", "Search", "ParentId", "Directory", "Project", "Subpath", "Cursor"],
                StringComparer.Ordinal)).IsTrue();
        await Assert.That(list.Envelope!.Kind).IsEqualTo(EnvelopeKind.CursorList);
        await Assert.That(list.Envelope.PayloadName).IsEqualTo("Sessions");
        await Assert.That(list.Envelope.PayloadTypeName).IsEqualTo("SessionInfo");
        await Assert.That(list.ErrorMap.Statuses[0].Tags.Select(static tag => tag.TypeName)
            .SequenceEqual(["InvalidCursorError", "InvalidRequestError"], StringComparer.Ordinal)).IsTrue();

        var create = sessions.Operations.Single(static operation => operation.MethodName == "CreateSessionAsync");
        await Assert.That(create.HttpMethod).IsEqualTo("post");
        await Assert.That(create.RequestBody!.TypeName).IsEqualTo("SessionCreateRequest");
        await Assert.That(create.RequestBody.IsOptional).IsTrue();
        await Assert.That(create.Envelope!.ResponseTypeName).IsEqualTo("SessionCreateResponse");
        await Assert.That(create.Envelope.PayloadName).IsEqualTo("Session");
    }

    [Test]
    public async Task Bind_Should_Create_The_Selected_Pinned_Shell_Plans()
    {
        var plan = await new BindingTestHost().BindPinnedAsync();

        var shells = plan.Clients.Single(static client => client.Name == "ShellsClient");
        await Assert.That(shells.ContainerName).IsEqualTo("Shells");
        await Assert.That(shells.Operations.Select(static operation => operation.MethodName)
            .SequenceEqual(["CreateShellAsync", "ListShellsAsync"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(shells.HandleFactory!.MethodName).IsEqualTo("GetShellClient");
        await Assert.That(shells.HandleFactory.Parameter.WireName).IsEqualTo("id");

        var createShell = shells.Operations.Single(static operation => operation.MethodName == "CreateShellAsync");
        await Assert.That(createShell.RequestBody!.TypeName).IsEqualTo("ShellCreateRequest");
        await Assert.That(createShell.QueryRequest!.RidesRequestBody).IsTrue();
        await Assert.That(createShell.QueryRequest.TypeName).IsEqualTo("ShellCreateRequest");
        await Assert.That(createShell.Envelope!.Kind).IsEqualTo(EnvelopeKind.DataLocation);
        await Assert.That(createShell.Envelope.PayloadTypeName).IsEqualTo("ShellInfo");
        await Assert.That(createShell.Envelope.LocationTypeName).IsEqualTo("LocationInfo");

        var listShells = shells.Operations.Single(static operation => operation.MethodName == "ListShellsAsync");
        await Assert.That(listShells.QueryRequest!.TypeName).IsEqualTo("ShellListRequest");
        await Assert.That(listShells.QueryRequest.RidesRequestBody).IsFalse();
        await Assert.That(listShells.QueryRequest.Properties.Single().Kind).IsEqualTo(QueryValueKind.Location);
        await Assert.That(listShells.Envelope!.Kind).IsEqualTo(EnvelopeKind.DataLocationList);
    }

    [Test]
    public async Task Bind_Should_Create_The_Selected_Pinned_Handle_Plans()
    {
        var plan = await new BindingTestHost().BindPinnedAsync();

        var session = plan.Clients.Single(static client => client.Name == "SessionClient");
        await Assert.That(session.ContainerName).IsEqualTo("Sessions");
        await Assert.That(session.HandleParameter!.WireName).IsEqualTo("sessionID");
        await Assert.That(session.HandleParameter.IsHandleParameter).IsTrue();
        await Assert.That(session.Operations.Select(static operation => operation.MethodName)
            .SequenceEqual(
                ["GetLogAsync", "GetMessageAsync", "GetSessionAsync", "ListMessagesAsync", "RemoveSessionAsync", "RenameSessionAsync"],
                StringComparer.Ordinal)).IsTrue();

        var remove = session.Operations.Single(static operation => operation.MethodName == "RemoveSessionAsync");
        await Assert.That(remove.HttpMethod).IsEqualTo("delete");
        await Assert.That(remove.Envelope!.Kind).IsEqualTo(EnvelopeKind.NoContent);
        await Assert.That(remove.Envelope.SuccessStatusCode).IsEqualTo(204);
        await Assert.That(remove.Envelope.ResponseTypeName).IsEqualTo("SessionRemoveResponse");

        var shell = plan.Clients.Single(static client => client.Name == "ShellClient");
        await Assert.That(shell.HandleParameter!.WireName).IsEqualTo("id");
        await Assert.That(shell.Operations.Select(static operation => operation.MethodName)
            .SequenceEqual(["GetShellAsync", "RemoveShellAsync", "TimeoutShellAsync"], StringComparer.Ordinal)).IsTrue();

        var timeout = shell.Operations.Single(static operation => operation.MethodName == "TimeoutShellAsync");
        await Assert.That(timeout.HttpMethod).IsEqualTo("patch");
        await Assert.That(timeout.RequestBody!.TypeName).IsEqualTo("ShellTimeoutRequest");
        await Assert.That(timeout.QueryRequest!.RidesRequestBody).IsTrue();

        var getShell = shell.Operations.Single(static operation => operation.MethodName == "GetShellAsync");
        await Assert.That(getShell.QueryRequest!.TypeName).IsEqualTo("ShellRequest");
        await Assert.That(getShell.Envelope!.Kind).IsEqualTo(EnvelopeKind.DataLocation);

        var messages = session.Operations.Single(static operation => operation.MethodName == "ListMessagesAsync");
        await Assert.That(messages.QueryRequest!.TypeName).IsEqualTo("MessageListRequest");
        await Assert.That(messages.QueryRequest.DerivesFromListRequest).IsTrue();
        await Assert.That(messages.Envelope!.Kind).IsEqualTo(EnvelopeKind.CursorList);
        await Assert.That(messages.Envelope.PayloadName).IsEqualTo("Messages");
        await Assert.That(messages.ErrorMap.Statuses.Select(static status => status.StatusCode)
            .SequenceEqual([400, 401, 404, 500])).IsTrue();

        var message = session.Operations.Single(static operation => operation.MethodName == "GetMessageAsync");
        await Assert.That(message.RouteTemplate).IsEqualTo("/api/session/{sessionID}/message/{messageID}");
        await Assert.That(message.RouteContainerName).IsEqualTo("Sessions");
        await Assert.That(message.RouteMemberName).IsEqualTo("GetMessage");
        await Assert.That(message.Summary).IsEqualTo("Get session message");
        await Assert.That(message.Parameters.Select(static parameter => parameter.Name)
            .SequenceEqual(["sessionId", "messageId"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(message.Parameters[0].IsHandleParameter).IsTrue();
        await Assert.That(message.Parameters[1].IsHandleParameter).IsFalse();
        await Assert.That(message.Parameters[1].WireName).IsEqualTo("messageID");
        await Assert.That(message.Parameters[1].TypeName).IsEqualTo("string");
        await Assert.That(message.Envelope!.ResponseTypeName).IsEqualTo("SessionMessageResponse");
        await Assert.That(message.Envelope.AdapterTypeName).IsEqualTo("SessionMessageResponseAdapter");
        await Assert.That(message.Envelope.PayloadName).IsEqualTo("Message");
        await Assert.That(message.Envelope.PayloadTypeName).IsEqualTo("ISessionMessageInfo");
        await Assert.That(message.Envelope.Kind).IsEqualTo(EnvelopeKind.Data);
        await Assert.That(message.ErrorMap.Statuses.Select(static status => status.StatusCode)
            .SequenceEqual([400, 401, 404])).IsTrue();
        await Assert.That(message.ErrorMap.Statuses[2].Tags.Select(static tag => tag.Tag)
            .SequenceEqual(["MessageNotFoundError", "SessionNotFoundError"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(message.ErrorMap.Statuses[2].Tags.All(static tag => tag.Tag == tag.TypeName)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Bind_A_Synthetic_Same_Shape_Group_Through_The_Same_Rules()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario());

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"))));

        var root = plan.Clients.Single(static client => client.Role == ClientRole.Root);
        await Assert.That(root.SubClients.Single().PropertyName).IsEqualTo("Gadgets");
        await Assert.That(root.SubClients.Single().TypeName).IsEqualTo("GadgetsClient");

        var gadgets = plan.Clients.Single(static client => client.Role == ClientRole.Collection);
        await Assert.That(gadgets.HandleFactory!.MethodName).IsEqualTo("GetGadgetClient");
        await Assert.That(gadgets.HandleFactory.Parameter.Name).IsEqualTo("gadgetId");

        var gadget = plan.Clients.Single(static client => client.Role == ClientRole.Handle);
        await Assert.That(gadget.Name).IsEqualTo("GadgetClient");
        var part = gadget.Operations.Single();
        await Assert.That(part.MethodName).IsEqualTo("GetPartAsync");
        await Assert.That(part.RouteTemplate).IsEqualTo("/api/gadget/{gadgetID}/part/{partID}");
        await Assert.That(part.RouteContainerName).IsEqualTo("Gadgets");
        await Assert.That(part.RouteMemberName).IsEqualTo("GetPart");
        await Assert.That(part.Parameters.Select(static parameter => parameter.Name)
            .SequenceEqual(["gadgetId", "partId"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(part.Envelope!.ResponseTypeName).IsEqualTo("GadgetPartResponse");
        await Assert.That(part.Envelope.AdapterTypeName).IsEqualTo("GadgetPartResponseAdapter");
        await Assert.That(part.Envelope.PayloadName).IsEqualTo("Part");
        await Assert.That(part.Envelope.PayloadTypeName).IsEqualTo("GadgetPart");
        await Assert.That(part.Envelope.Kind).IsEqualTo(EnvelopeKind.Data);
        await Assert.That(part.ErrorMap.Statuses.Single().StatusCode).IsEqualTo(404);
        await Assert.That(part.ErrorMap.Statuses.Single().Tags.Single().TypeName).IsEqualTo("GadgetMissingError");
    }

    [Test]
    public async Task Bind_Should_Keep_A_Group_Without_Handle_Declaration_Flat()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.item", path: "/api/widget/{sessionID}/item", configure: operation => operation
                .Parameter("sessionID", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.item"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null))));

        await Assert.That(plan.Clients.Any(static client => client.Role == ClientRole.Handle)).IsFalse();
        var widgets = plan.Clients.Single(static client => client.Role == ClientRole.Collection);
        await Assert.That(widgets.HandleFactory).IsNull();
        var item = widgets.Operations.Single();
        await Assert.That(item.MethodName).IsEqualTo("GetItemAsync");
        await Assert.That(item.Parameters.Single().Name).IsEqualTo("sessionId");
        await Assert.That(item.Parameters.Single().IsHandleParameter).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Keep_Collection_Operations_On_The_Collection_Client()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario(spec => spec
            .WithOperation("v2.gadget.overview", path: "/api/gadget-overview", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("GadgetPart")))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part", "v2.gadget.overview"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"))));

        var gadgets = plan.Clients.Single(static client => client.Role == ClientRole.Collection);
        await Assert.That(gadgets.Operations.Single().MethodName).IsEqualTo("GetOverviewAsync");
        var gadget = plan.Clients.Single(static client => client.Role == ClientRole.Handle);
        await Assert.That(gadget.Operations.Single().MethodName).IsEqualTo("GetPartAsync");
    }

    [Test]
    public async Task Bind_Should_Order_Parameters_By_Route_Template_Position()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario(parametersReversed: true));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"))));

        var part = plan.Clients.Single(static client => client.Role == ClientRole.Handle).Operations.Single();
        await Assert.That(part.Parameters.Select(static parameter => parameter.WireName)
            .SequenceEqual(["gadgetID", "partID"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Model_Colliding_With_A_Spine_Type_Name()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ListCursor", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.item", path: "/api/widget/item", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ListCursor")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.item"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("spine", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Unsupported_Http_Method()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.reset", method: "put", path: "/api/health-reset", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.reset", "HTTP method");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Post_Without_A_Request_Body()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.reset", method: "post", path: "/api/health-reset", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.reset", "must carry a request body");
    }

    [Test]
    public async Task Bind_Should_Bind_A_Json_Request_Body_Into_A_Request_Model()
    {
        var document = await BindingTestHost.IngestAsync(WidgetCreateScenario(body => body.Type("object")
            .AdditionalPropertiesFalse()
            .Property("id", property => property.AnyOf(
                static branch => branch.Type("string"),
                static branch => branch.Type("null")))
            .Property("title", property => property.AnyOf(
                static branch => branch.Type("string"),
                static branch => branch.Type("null")))));

        var plan = BindWidgets(document, "v2.widget.create");

        var create = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(create.HttpMethod).IsEqualTo("post");
        await Assert.That(create.RequestBody).IsNotNull();
        await Assert.That(create.RequestBody!.TypeName).IsEqualTo("WidgetCreateRequest");
        await Assert.That(create.RequestBody.ParameterName).IsEqualTo("request");
        await Assert.That(create.RequestBody.IsOptional).IsTrue();
        await Assert.That(plan.Models.Select(static model => model.Name)).Contains("WidgetCreateRequest");
        await Assert.That(plan.Registry.TypeNames).Contains("WidgetCreateRequest");
    }

    [Test]
    public async Task Bind_Should_Require_The_Request_Parameter_When_The_Body_Has_Required_Properties()
    {
        var document = await BindingTestHost.IngestAsync(WidgetCreateScenario(body => body.Type("object")
            .Property("title", property => property.Type("string"), required: true)));

        var plan = BindWidgets(document, "v2.widget.create");

        var create = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(create.RequestBody!.IsOptional).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Non_Json_Request_Body()
    {
        var document = await BindingTestHost.IngestAsync(WidgetCreateScenario(
            body => body.Type("object").Property("title", property => property.Type("string")),
            mediaType: "text/plain"));

        await AssertWidgetRefusalAsync(document, "JSON", "v2.widget.create");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Non_Object_Request_Body()
    {
        var document = await BindingTestHost.IngestAsync(WidgetCreateScenario(body => body.Type("string")));

        await AssertWidgetRefusalAsync(document, "object schema", "v2.widget.create");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Optional_Request_Body()
    {
        var document = await BindingTestHost.IngestAsync(WidgetCreateScenario(
            body => body.Type("object").Property("title", property => property.Type("string")),
            required: false));

        await AssertWidgetRefusalAsync(document, "declared required", "v2.widget.create");
    }

    [Test]
    public async Task Bind_Should_Bind_Optional_Nullable_Query_Parameters_Into_A_Flat_Query_Request()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("limit", "query", QueryScenarioData.NullableString)
            .Parameter("order", "query", QueryScenarioData.NullableOrderEnum)
            .Parameter("cursor", "query", QueryScenarioData.NullableString)
            .Parameter("search", "query", QueryScenarioData.NullableString)
            .Parameter("parentID", "query", QueryScenarioData.NullableParentFilter)));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.QueryRequest).IsNotNull();
        await Assert.That(list.QueryRequest!.TypeName).IsEqualTo("WidgetListRequest");
        await Assert.That(list.QueryRequest.DerivesFromListRequest).IsFalse();
        await Assert.That(list.QueryRequest.Properties.Select(static property => property.WireName)
            .SequenceEqual(["limit", "order", "cursor", "search", "parentID"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(list.QueryRequest.Properties.Select(static property => property.PropertyName)
            .SequenceEqual(["Limit", "Order", "Cursor", "Search", "ParentId"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(list.QueryRequest.Properties.Select(static property => property.Kind)
            .SequenceEqual([
                QueryValueKind.PositiveCount,
                QueryValueKind.ListOrder,
                QueryValueKind.Text,
                QueryValueKind.Text,
                QueryValueKind.SessionParentFilter,
            ])).IsTrue();
        await Assert.That(list.QueryRequest.Properties.All(static property => !property.IsInherited)).IsTrue();
        await Assert.That(list.Parameters).IsEmpty();
    }

    [Test]
    public async Task Bind_Should_Keep_Query_Parameter_Schemas_Out_Of_The_Model_Closure()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("order", "query", QueryScenarioData.NullableOrderEnum)
            .Parameter("parentID", "query", QueryScenarioData.NullableParentFilter)));

        var plan = BindWidgets(document);

        await Assert.That(plan.Models.Select(static model => model.Name)
            .SequenceEqual(["WidgetInfo"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(plan.Unions).IsEmpty();
        await Assert.That(plan.Registry.TypeNames
            .SequenceEqual(["WidgetInfo"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Derive_The_Query_Request_From_The_List_Request_Base_When_The_Trio_Matches_Exactly()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("limit", "query", QueryScenarioData.NullableString)
            .Parameter("order", "query", QueryScenarioData.NullableOrderEnum)
            .Parameter("cursor", "query", QueryScenarioData.NullableString)));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.QueryRequest!.TypeName).IsEqualTo("WidgetListRequest");
        await Assert.That(list.QueryRequest.DerivesFromListRequest).IsTrue();
        await Assert.That(list.QueryRequest.Properties.Select(static property => property.PropertyName)
            .SequenceEqual(["Limit", "Order", "Cursor"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(list.QueryRequest.Properties.All(static property => property.IsInherited)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Keep_The_Query_Request_Flat_When_The_Trio_Has_Extra_Parameters()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("limit", "query", QueryScenarioData.NullableString)
            .Parameter("order", "query", QueryScenarioData.NullableOrderEnum)
            .Parameter("cursor", "query", QueryScenarioData.NullableString)
            .Parameter("search", "query", QueryScenarioData.NullableString)));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.QueryRequest!.DerivesFromListRequest).IsFalse();
        await Assert.That(list.QueryRequest.Properties.All(static property => !property.IsInherited)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Keep_The_Query_Request_Flat_When_The_Trio_Is_Incomplete()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("limit", "query", QueryScenarioData.NullableString)
            .Parameter("cursor", "query", QueryScenarioData.NullableString)));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.QueryRequest!.DerivesFromListRequest).IsFalse();
        await Assert.That(list.QueryRequest.Properties.Select(static property => property.PropertyName)
            .SequenceEqual(["Limit", "Cursor"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Leave_The_Query_Request_Absent_When_An_Operation_Has_No_Query_Parameters()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario());

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"))));

        var part = plan.Clients.Single(static client => client.Role == ClientRole.Handle).Operations.Single();
        await Assert.That(part.QueryRequest).IsNull();
    }

    [Test]
    public async Task Bind_Should_Bind_A_Cursor_List_Envelope()
    {
        var document = await BindingTestHost.IngestAsync(CursorListScenario());

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.Envelope!.Kind).IsEqualTo(EnvelopeKind.CursorList);
        await Assert.That(list.Envelope.PayloadTypeName).IsEqualTo("WidgetInfo");
        await Assert.That(list.Envelope.EnvelopeDtoTypeName).IsEqualTo("WidgetListResponseEnvelope");
        await Assert.That(plan.Models.Select(static model => model.Name)
            .SequenceEqual(["WidgetInfo"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(plan.Registry.TypeNames
            .SequenceEqual(["WidgetInfo", "WidgetListResponseEnvelope"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Bind_A_Data_Location_Envelope()
    {
        var document = await BindingTestHost.IngestAsync(DataLocationScenario());

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.Envelope!.Kind).IsEqualTo(EnvelopeKind.DataLocation);
        await Assert.That(list.Envelope.PayloadTypeName).IsEqualTo("WidgetInfo");
        await Assert.That(list.Envelope.LocationTypeName).IsEqualTo("PlaceInfo");
        await Assert.That(list.Envelope.EnvelopeDtoTypeName).IsEqualTo("WidgetListResponseEnvelope");
        await Assert.That(plan.Models.Select(static model => model.Name)
            .SequenceEqual(["PlaceInfo", "WidgetInfo"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(plan.Registry.TypeNames.Contains("WidgetListResponseEnvelope", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Bind_A_Data_Location_List_Envelope()
    {
        var document = await BindingTestHost.IngestAsync(DataLocationScenario(
            data: static property => property.Type("array").Items(static item => item.Ref("WidgetInfo"))));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.Envelope!.Kind).IsEqualTo(EnvelopeKind.DataLocationList);
        await Assert.That(list.Envelope.PayloadTypeName).IsEqualTo("WidgetInfo");
        await Assert.That(list.Envelope.LocationTypeName).IsEqualTo("PlaceInfo");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Data_Location_Envelope_With_An_Optional_Location()
    {
        var document = await BindingTestHost.IngestAsync(DataLocationScenario(locationRequired: false));

        await AssertWidgetRefusalAsync(document, "require exactly");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Data_Location_Sibling_Without_A_Named_Schema()
    {
        var document = await BindingTestHost.IngestAsync(DataLocationScenario(
            location: static property => property.Type("object")
                .Property("directory", static inner => inner.Type("string"), required: true)));

        await AssertWidgetRefusalAsync(document, "location sibling");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Data_Location_List_Of_Promoted_Inline_Items()
    {
        var document = await BindingTestHost.IngestAsync(DataLocationScenario(
            data: static property => property.Type("array").Items(static item => item.Type("object")
                .Property("id", static inner => inner.Type("string"), required: true))));

        await AssertWidgetRefusalAsync(document, "named component schema");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Envelope_Dto_Name_Colliding_With_A_Model()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true)
                .Property("extra", property => property.Ref("WidgetListResponseEnvelope"), required: true))
            .WithSchema("WidgetListResponseEnvelope", schema => schema.Type("object")
                .Property("note", property => property.Type("string"), required: true))
            .WithSchema("WidgetsResponse", schema => schema.Type("object")
                .AdditionalPropertiesFalse()
                .Property("data", property => property.Type("array")
                    .Items(static item => item.Ref("WidgetInfo")), required: true)
                .Property("cursor", cursor => cursor.Type("object")
                    .AdditionalPropertiesFalse()
                    .Property("previous", static property => property.AnyOf(
                        static branch => branch.Type("string"),
                        static branch => branch.Type("null")))
                    .Property("next", static property => property.AnyOf(
                        static branch => branch.Type("string"),
                        static branch => branch.Type("null"))), required: true))
            .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetsResponse")))));

        var exception = Assert.Throws<BindingException>(() => _ = BindWidgets(document));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("envelope DTO", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Name_A_Component_Request_Body_From_The_Operation()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("Widget.CreatePayload", schema => schema.Type("object")
                .Property("title", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget", configure: operation => operation
                .RequestBody("application/json", body => body.Ref("Widget.CreatePayload"), required: true)
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))));

        var plan = BindWidgets(document, "v2.widget.create");

        var create = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(create.RequestBody!.TypeName).IsEqualTo("WidgetCreateRequest");
        await Assert.That(plan.Models.Any(static model => model.Name == "WidgetCreateRequest")).IsTrue();
        await Assert.That(plan.Models.Any(static model => model.Name == "WidgetCreatePayload")).IsFalse();
        await Assert.That(plan.Registry.TypeNames.Contains("WidgetCreateRequest", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Not_Let_A_Pending_Operation_Rename_A_Shared_Component()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetShared", schema => schema.Type("object")
                .Property("note", property => property.Type("string"), required: true))
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true)
                .Property("shared", property => property.Ref("WidgetShared"), required: true))
            .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget-create", configure: operation => operation
                .RequestBody("application/json", body => body.Ref("WidgetShared"), required: true)
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))));

        var plan = BindWidgets(document);

        await Assert.That(plan.Models.Any(static model => model.Name == "WidgetShared")).IsTrue();
        await Assert.That(plan.Models.Any(static model => model.Name == "WidgetCreateRequest")).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Operation_Mixing_A_Body_And_Query_Parameters()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("Widget.CreatePayload", schema => schema.Type("object")
                .Property("title", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget", configure: operation => operation
                .Parameter("search", "query", static schema => schema.AnyOf(
                    static branch => branch.Type("string"),
                    static branch => branch.Type("null")), required: false)
                .RequestBody("application/json", body => body.Ref("Widget.CreatePayload"), required: true)
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))));

        await AssertWidgetRefusalAsync(document, "request body and query", "v2.widget.create");
    }

    [Test]
    public async Task Bind_Should_Bind_A_Component_Data_Envelope_Without_Modeling_The_Wrapper()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("WidgetResponse", schema => schema.Type("object")
                .AdditionalPropertiesFalse()
                .Property("data", property => property.Ref("WidgetInfo"), required: true))
            .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetResponse")))));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.Envelope!.Kind).IsEqualTo(EnvelopeKind.Data);
        await Assert.That(list.Envelope.PayloadTypeName).IsEqualTo("WidgetInfo");
        await Assert.That(plan.Models.Select(static model => model.Name)
            .SequenceEqual(["WidgetInfo"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Cursor_List_With_A_Malformed_Cursor()
    {
        var document = await BindingTestHost.IngestAsync(CursorListScenario(cursor => cursor.Type("object")
            .AdditionalPropertiesFalse()
            .Property("previous", property => property.AnyOf(
                static branch => branch.Type("string"),
                static branch => branch.Type("null")), required: true)
            .Property("next", property => property.AnyOf(
                static branch => branch.Type("string"),
                static branch => branch.Type("null")))));

        await AssertWidgetRefusalAsync(document, "cursor object");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Cursor_List_Whose_Items_Are_Not_Component_References()
    {
        var document = await BindingTestHost.IngestAsync(CursorListScenario(items: items => items.Type("object")
            .Property("id", property => property.Type("string"), required: true)));

        await AssertWidgetRefusalAsync(document, "array of a named component schema");
    }

    [Test]
    public async Task Bind_Should_Merge_Groups_Sharing_A_Client_Name()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario(spec => spec
            .WithOperation("v2.gizmo.list", path: "/api/gadget/{gadgetID}/gizmo", configure: operation => operation
                .Parameter("gadgetID", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("data", property => property.Ref("GadgetPart"), required: true)))));

        var groups = new Dictionary<string, GroupCuration>(StringComparer.Ordinal)
        {
            ["gadget"] = ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"),
            ["gizmo"] = ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"),
        };

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part", "v2.gizmo.list"),
            Curation(groups));

        var root = plan.Clients.Single(static client => client.Role == ClientRole.Root);
        await Assert.That(root.SubClients.Single().TypeName).IsEqualTo("GadgetsClient");
        var collection = plan.Clients.Single(static client => client.Role == ClientRole.Collection);
        await Assert.That(collection.Operations).IsEmpty();
        var handle = plan.Clients.Single(static client => client.Role == ClientRole.Handle);
        await Assert.That(handle.Operations.Select(static operation => operation.MethodName)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(["GetPartAsync", "ListGizmosAsync"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(handle.Operations.All(static operation => operation.RouteContainerName == "Gadgets")).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_Merged_Groups_With_Diverging_Handles()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario(spec => spec
            .WithOperation("v2.gizmo.list", path: "/api/gadget/{gadgetID}/gizmo", configure: operation => operation
                .Parameter("gadgetID", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("data", property => property.Ref("GadgetPart"), required: true)))));

        var groups = new Dictionary<string, GroupCuration>(StringComparer.Ordinal)
        {
            ["gadget"] = ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID"),
            ["gizmo"] = ClientGroup(clientName: "Gadgets", handleName: null, handleParameter: null),
        };

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part", "v2.gizmo.list"),
            Curation(groups)));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Curation
            && error.Problem.Contains("identical handle", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Bind_A_500_Error_Arm()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("UnknownFailure", schema => schema.Type("object")
                .Property("_tag", property => property.Type("string").Enum("UnknownFailure"), required: true)
                .Property("message", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo"))
                .Response(500, "application/json", schema => schema.Ref("UnknownFailure")))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(Groups("health", RootGroup())));

        var health = plan.Clients.Single(static client => client.Role == ClientRole.Root).Operations.Single();
        var status = health.ErrorMap.Statuses.Single();
        await Assert.That(status.StatusCode).IsEqualTo(500);
        await Assert.That(status.Tags.Single().TypeName).IsEqualTo("UnknownFailure");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Required_Query_Parameter()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("limit", "query", QueryScenarioData.NullableString, required: true)));

        await AssertWidgetRefusalAsync(document, "must be optional");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Query_Parameter_That_Does_Not_Admit_Null()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("limit", "query", schema => schema.Type("string"))));

        await AssertWidgetRefusalAsync(document, "must admit null");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Query_Enum_Outside_The_Order_Profile()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("order", "query", schema => schema.AnyOf(
                branch => branch.Type("string").Enum("asc", "desc", "shuffled"),
                branch => branch.Type("null")))));

        await AssertWidgetRefusalAsync(document, "unsupported schema shape");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Object_Query_Parameter()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("filter", "query", schema => schema.AnyOf(
                branch => branch.Type("object")
                    .Property("name", property => property.Type("string"), required: true),
                branch => branch.Type("null")))));

        await AssertWidgetRefusalAsync(document, "unsupported schema shape");
    }

    [Test]
    public async Task Bind_Should_Record_A_Curated_Mutually_Exclusive_Query_Pair()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("order", "query", QueryScenarioData.NullableOrderEnum)
            .Parameter("cursor", "query", QueryScenarioData.NullableString)));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.list"),
            Curation(
                Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)),
                mutuallyExclusiveQueries: [ExclusiveQuery("v2.widget.list", "order", "cursor")]));

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        var pair = list.QueryRequest!.MutuallyExclusivePairs.Single();
        await Assert.That(pair.FirstWireName).IsEqualTo("order");
        await Assert.That(pair.SecondWireName).IsEqualTo("cursor");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Mutually_Exclusive_Row_When_The_Operation_Binds_No_Query_Surface()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(static _ => { }));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.list"),
            Curation(
                Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)),
                mutuallyExclusiveQueries: [ExclusiveQuery("v2.widget.list", "order", "cursor")])));

        await Assert.That(exception.Errors.Any(static error =>
            error.Problem.Contains("does not carry query parameter", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Mutually_Exclusive_Row_Naming_An_Absent_Parameter()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("order", "query", QueryScenarioData.NullableOrderEnum)));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.list"),
            Curation(
                Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)),
                mutuallyExclusiveQueries: [ExclusiveQuery("v2.widget.list", "order", "cursor")])));

        await Assert.That(exception.Errors.Any(static error =>
            error.Problem.Contains("does not carry query parameter", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Merge_A_Location_Query_Into_The_Request_Body_Model()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget", configure: operation => operation
                .Parameter("location", "query", QueryScenarioData.NullableLocationSelector, deepObject: true)
                .RequestBody("application/json", body => body.Type("object")
                    .Property("title", property => property.Type("string"), required: true), required: true)
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))));

        var plan = BindWidgets(document, "v2.widget.create");

        var create = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(create.RequestBody!.TypeName).IsEqualTo("WidgetCreateRequest");
        await Assert.That(create.QueryRequest!.RidesRequestBody).IsTrue();
        await Assert.That(create.QueryRequest.TypeName).IsEqualTo("WidgetCreateRequest");
        await Assert.That(create.QueryRequest.Properties.Single().Kind).IsEqualTo(QueryValueKind.Location);
        var model = (ObjectModelPlan)plan.Models.Single(static model => model.Name is "WidgetCreateRequest");
        await Assert.That(model.RequestQueryProperties.Single().PropertyName).IsEqualTo("Location");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Deep_Object_Query_Parameter()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("location", "query", QueryScenarioData.NullableString, deepObject: true)));

        await AssertWidgetRefusalAsync(document, "deep-object");
    }

    [Test]
    public async Task Bind_Should_Bind_The_Location_Selector_Deep_Object()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("location", "query", QueryScenarioData.NullableLocationSelector, deepObject: true)));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        var property = list.QueryRequest!.Properties.Single();
        await Assert.That(property.Kind).IsEqualTo(QueryValueKind.Location);
        await Assert.That(property.PropertyName).IsEqualTo("Location");
        await Assert.That(plan.Models.Select(static model => model.Name)
            .SequenceEqual(["WidgetInfo"], StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Deep_Object_Outside_The_Location_Selector_Shape()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("location", "query", QueryScenarioData.NullableSelectorWithExtraMember, deepObject: true)));

        await AssertWidgetRefusalAsync(document, "location selector");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Required_Location_Selector()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(operation => operation
            .Parameter("location", "query", QueryScenarioData.NullableLocationSelector, required: true, deepObject: true)));

        await AssertWidgetRefusalAsync(document, "location selector");
    }

    [Test]
    public async Task Bind_Should_Bind_A_Delete_Operation_With_A_No_Content_Success()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.widget.remove", method: "delete", path: "/api/widget/{id}", configure: operation =>
            {
                _ = operation.Parameter("id", "path", schema => schema.Type("string"), required: true)
                    .WithoutResponse(200)
                    .Response(204);
            })));

        var plan = BindWidgets(document, "v2.widget.remove");

        var remove = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(remove.MethodName).IsEqualTo("RemoveWidgetAsync");
        await Assert.That(remove.HttpMethod).IsEqualTo("delete");
        await Assert.That(remove.Envelope!.Kind).IsEqualTo(EnvelopeKind.NoContent);
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Request_Body_On_A_Delete_Operation()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.widget.remove", method: "delete", path: "/api/widget", configure: operation => operation
                .RequestBody("application/json", schema => schema.Type("object")
                    .Property("id", property => property.Type("string"), required: true))
                .WithoutResponse(200)
                .Response(204))));

        await AssertWidgetRefusalAsync(document, "must not carry a request body", "v2.widget.remove");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Patch_Operation_Without_A_Request_Body()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.timeout", method: "patch", path: "/api/widget", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))));

        await AssertWidgetRefusalAsync(document, "must carry a request body", "v2.widget.timeout");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Request_Body_On_A_Get_Operation()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .RequestBody("application/json", schema => schema.Type("object")
                    .Property("value", property => property.Type("string"), required: true))
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "must not carry a request body");
    }

    [Test]
    public async Task Bind_Should_Refuse_Multiple_Success_Statuses()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo"))
                .Response(201, "application/json", schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "exactly one success response");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Non_200_Success()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .WithoutResponse(200)
                .Response(201, "application/json", schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "status 200");
    }

    [Test]
    public async Task Bind_Should_Bind_A_No_Content_Success_Into_A_Payload_Free_Envelope()
    {
        var document = await BindingTestHost.IngestAsync(NoContentScenario());

        var plan = BindWidgets(document, "v2.widget.create");

        var create = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(create.Envelope!.Kind).IsEqualTo(EnvelopeKind.NoContent);
        await Assert.That(create.Envelope.SuccessStatusCode).IsEqualTo(204);
        await Assert.That(create.Envelope.ResponseTypeName).IsEqualTo("WidgetCreateResponse");
        await Assert.That(create.Envelope.PayloadName).IsNull();
        await Assert.That(create.Envelope.PayloadTypeName).IsNull();
        await Assert.That(create.Envelope.EnvelopeDtoTypeName).IsNull();
        await Assert.That(plan.Registry.TypeNames.Contains("WidgetCreateResponseEnvelope", StringComparer.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Bind_Should_Record_The_200_Success_Status_On_The_Envelope()
    {
        var document = await BindingTestHost.IngestAsync(WidgetListScenario(static _ => { }));

        var plan = BindWidgets(document);

        var list = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(list.Envelope!.SuccessStatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task Bind_Should_Refuse_A_No_Content_Success_Carrying_Content()
    {
        var document = await BindingTestHost.IngestAsync(NoContentScenario(static operation => _ = operation
            .WithoutResponse(204)
            .Response(204, "application/json", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))));

        await AssertWidgetRefusalAsync(document, "must not carry content", "v2.widget.create");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Success_Without_Json_Content()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.health.get")));

        await AssertOperationRefusalAsync(document, "v2.health.get", "JSON schema");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Unsupported_Envelope_Shape()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("data", property => property.Ref("ItemInfo"), required: true)
                    .Property("hasMore", property => property.Type("boolean"), required: true)))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "envelope shape");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Bare_Success_Without_A_Named_Schema()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("value", property => property.Type("string"), required: true)))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "named schema");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Untagged_Error_Response()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("PlainProblem", schema => schema.Type("object")
                .Property("message", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo"))
                .Response(404, "application/json", schema => schema.Ref("PlainProblem")))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "tagged error");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Event_Stream_Whose_Frame_Is_Not_Declared()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .SseResponse(schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "event frame");
    }

    [Test]
    public async Task Bind_Should_Bind_An_Event_Stream_Into_A_Stream_Plan()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("ItemInfoJsonString", schema => schema.Type("string")
                .Raw("contentMediaType", "\"application/json\"")
                .Raw("contentSchema", "{\"$ref\":\"#/components/schemas/ItemInfo\"}"))
            .WithSchema("ItemFrame", schema => schema.Type("object")
                .Property("id", property => property.AnyOf(
                    branch => branch.Type("string"),
                    branch => branch.Type("null")), required: true)
                .Property("event", property => property.Type("string"), required: true)
                .Property("data", property => property.Ref("ItemInfoJsonString"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .SseResponse(
                    schema => schema.Ref("ItemFrame"),
                    "{\"encoding\":\"sse\",\"failureEvent\":\"effect/httpapi/stream/failure\"}"))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get"),
            Curation(new Dictionary<string, GroupCuration>(StringComparer.Ordinal) { ["health"] = RootGroup(), }));

        var operation = plan.Clients.SelectMany(static client => client.Operations).Single();
        await Assert.That(operation.Envelope).IsNull();
        await Assert.That(operation.Stream).IsNotNull();
        await Assert.That(operation.Stream!.PayloadTypeName).IsEqualTo("ItemInfo");
        await Assert.That(operation.Stream.FailureEventName).IsEqualTo("effect/httpapi/stream/failure");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Wildcard_Path()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", path: "/api/health/*", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        await AssertOperationRefusalAsync(document, "v2.health.get", "wildcard");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Reserved_Parameter_Name()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.item", path: "/api/widget/{request}", configure: operation => operation
                .Parameter("request", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.item"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("reserved by the emitted method signature", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Admit_A_Path_Parameter_Named_Options()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.item", path: "/api/widget/{options}", configure: operation => operation
                .Parameter("options", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))));

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.item"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null))));

        var item = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(item.Parameters.Single().Name).IsEqualTo("options");
    }

    [Test]
    public async Task Bind_Should_Refuse_An_Operation_Declaring_Both_A_Body_And_Query_Parameters()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget", configure: operation => operation
                .Parameter("dryRun", "query", QueryScenarioData.NullableString)
                .RequestBody("application/json", body => body.Type("object")
                    .Property("title", property => property.AnyOf(
                        static branch => branch.Type("string"),
                        static branch => branch.Type("null"))), required: true)
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo")))));

        await AssertWidgetRefusalAsync(document, "request body and query", "v2.widget.create");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Response_Type_Shadowing_A_Model()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("WidgetStateResponse", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.state", path: "/api/widget-state", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetStateResponse")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.state"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("WidgetStateResponse", StringComparison.Ordinal)
            && error.Problem.Contains("response type", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Client_Name_Colliding_With_The_Spine()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario());

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "ListCursor", handleParameter: "gadgetID")))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("ListCursor", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_Colliding_Method_Names_On_A_Client()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario(spec => spec
            .WithOperation("v2.gadget.part.get", path: "/api/gadget/{gadgetID}/part-alias/{partID}", configure: operation => operation
                .Parameter("gadgetID", "path", schema => schema.Type("string"), required: true)
                .Parameter("partID", "path", schema => schema.Type("string"), required: true)
                .Response(200, "application/json", schema => schema.Type("object")
                    .Property("data", property => property.Ref("GadgetPart"), required: true)))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part", "v2.gadget.part.get"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID")))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("GetPartAsync", StringComparison.Ordinal))).IsTrue();
        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("GadgetPartResponse", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_Colliding_Route_Members_In_A_Container()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario(spec => spec
            .WithOperation("v2.gadget.part.get", path: "/api/gadget-part", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("GadgetPart")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part", "v2.gadget.part.get"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID")))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("route member", StringComparison.Ordinal)
            && error.Problem.Contains("GetPart", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Refuse_Colliding_Client_Type_Names()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario());

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part"),
            Curation(Groups("gadget", ClientGroup(clientName: "Gadget", handleName: "GadgetClient", handleParameter: "gadgetID")))));

        await Assert.That(exception.Errors.Any(static error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains("GadgetClient", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Apply_A_Curated_Payload_Name_Override()
    {
        var document = await BindingTestHost.IngestAsync(GadgetScenario());
        var payloadNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v2.gadget.part"] = "Component",
        };

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.gadget.part"),
            Curation(
                Groups("gadget", ClientGroup(clientName: "Gadgets", handleName: "GadgetClient", handleParameter: "gadgetID")),
                payloadNames));

        var part = plan.Clients.Single(static client => client.Role == ClientRole.Handle).Operations.Single();
        await Assert.That(part.Envelope!.PayloadName).IsEqualTo("Component");
    }

    [Test]
    public async Task Bind_Should_Refuse_A_Payload_Name_Colliding_With_The_Response_Spine()
    {
        var document = await BindingTestHost.IngestAsync(StatusScenario());

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.status"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)))));

        await Assert.That(exception.Errors.Single(static error => error.Category == BindingErrorCategory.Naming).Problem)
            .Contains("Status");
    }

    [Test]
    [Arguments("EqualityContract")]
    [Arguments("ToString")]
    [Arguments("GetHashCode")]
    [Arguments("PrintMembers")]
    public async Task Bind_Should_Refuse_A_Payload_Name_Colliding_With_Record_Members(string payloadName)
    {
        var document = await BindingTestHost.IngestAsync(StatusScenario());
        var payloadNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v2.widget.status"] = payloadName,
        };

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.status"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)), payloadNames)));

        await Assert.That(exception.Errors.Any(error => error.Category == BindingErrorCategory.Naming
            && error.Problem.Contains(payloadName, StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Bind_Should_Accept_An_Override_That_Resolves_A_Reserved_Payload_Collision()
    {
        var document = await BindingTestHost.IngestAsync(StatusScenario());
        var payloadNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v2.widget.status"] = "WidgetStatus",
        };

        var plan = new BindingTestHost().Bind(
            document,
            Selection("v2.widget.status"),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null)), payloadNames));

        var status = plan.Clients.Single(static client => client.Role == ClientRole.Collection).Operations.Single();
        await Assert.That(status.Envelope!.PayloadName).IsEqualTo("WidgetStatus");
    }

    [Test]
    public async Task Bind_Should_Report_Failures_For_Every_Selected_Operation()
    {
        var document = await BindingTestHost.IngestAsync(SpecScenario.Define(spec => spec
            .WithSchema("ItemInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.health.get", configure: operation => operation
                .Parameter("limit", "query", schema => schema.Type("string"))
                .Response(200, "application/json", schema => schema.Ref("ItemInfo")))
            .WithOperation("v2.health.probe", path: "/api/health-probe", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("ItemInfo"))
                .Response(302, "application/json", schema => schema.Ref("ItemInfo")))));

        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection("v2.health.get", "v2.health.probe"),
            Curation(Groups("health", RootGroup()))));

        var subjects = exception.Errors
            .Where(static error => error.Category == BindingErrorCategory.Operation)
            .Select(static error => error.Subject)
            .ToArray();
        await Assert.That(subjects).Contains("v2.health.get");
        await Assert.That(subjects).Contains("v2.health.probe");
    }

    private static async Task AssertOperationRefusalAsync(SpecDocument document, string operationId, string expectedProblem)
    {
        var exception = Assert.Throws<BindingException>(() => _ = new BindingTestHost().Bind(
            document,
            Selection(operationId),
            Curation(Groups("health", RootGroup()))));

        await Assert.That(exception.Errors.Any(error => error.Category == BindingErrorCategory.Operation
            && string.Equals(error.Subject, operationId, StringComparison.Ordinal)
            && error.Problem.Contains(expectedProblem, StringComparison.Ordinal))).IsTrue();
    }

    private static SpecScenario WidgetListScenario(Action<OperationBuilder> configureParameters) =>
        SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.list", path: "/api/widget", configure: operation =>
            {
                configureParameters(operation);
                _ = operation.Response(200, "application/json", schema => schema.Ref("WidgetInfo"));
            }));

    private static SpecScenario CursorListScenario(Action<SchemaBuilder>? cursor = null, Action<SchemaBuilder>? items = null) =>
        SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("WidgetsResponse", schema => schema.Type("object")
                .AdditionalPropertiesFalse()
                .Property("data", property => property
                    .Type("array")
                    .Items(items ?? (static item => item.Ref("WidgetInfo"))), required: true)
                .Property("cursor", cursor ?? DefaultCursor, required: true))
            .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetsResponse"))));

    private static void DefaultCursor(SchemaBuilder cursor) => cursor.Type("object")
        .AdditionalPropertiesFalse()
        .Property("previous", static property => property.AnyOf(
            static branch => branch.Type("string"),
            static branch => branch.Type("null")))
        .Property("next", static property => property.AnyOf(
            static branch => branch.Type("string"),
            static branch => branch.Type("null")));

    private static SpecScenario WidgetCreateScenario(Action<SchemaBuilder> configureBody,
        string mediaType = "application/json", bool required = true) =>
        SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget", configure: operation => operation
                .RequestBody(mediaType, configureBody, required)
                .Response(200, "application/json", schema => schema.Ref("WidgetInfo"))));

    private static SpecScenario DataLocationScenario(Action<SchemaBuilder>? data = null,
        Action<SchemaBuilder>? location = null, bool locationRequired = true) =>
        SpecScenario.Define(spec => spec
            .WithSchema("WidgetInfo", schema => schema.Type("object")
                .Property("id", property => property.Type("string"), required: true))
            .WithSchema("PlaceInfo", schema => schema.Type("object")
                .Property("directory", property => property.Type("string"), required: true))
            .WithSchema("WidgetEnvelope", schema => schema.Type("object")
                .AdditionalPropertiesFalse()
                .Property("location", location ?? (static property => property.Ref("PlaceInfo")), required: locationRequired)
                .Property("data", data ?? (static property => property.Ref("WidgetInfo")), required: true))
            .WithOperation("v2.widget.list", path: "/api/widget", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetEnvelope"))));

    private static SpecScenario NoContentScenario(Action<OperationBuilder>? configure = null) =>
        SpecScenario.Define(spec => spec
            .WithOperation("v2.widget.create", method: "post", path: "/api/widget", configure: operation =>
            {
                _ = operation
                    .RequestBody("application/json", schema => schema.Type("object")
                        .Property("title", property => property.Type("string"), required: true), required: true)
                    .WithoutResponse(200)
                    .Response(204);
                configure?.Invoke(operation);
            }));

    private static EmitPlan BindWidgets(SpecDocument document, string operationId = "v2.widget.list") =>
        new BindingTestHost().Bind(
            document,
            Selection(operationId),
            Curation(Groups("widget", ClientGroup(clientName: "Widgets", handleName: null, handleParameter: null))));

    private static async Task AssertWidgetRefusalAsync(SpecDocument document, string expectedProblem,
        string operationId = "v2.widget.list")
    {
        var exception = Assert.Throws<BindingException>(() => _ = BindWidgets(document, operationId));

        await Assert.That(exception.Errors.Any(error => error.Category == BindingErrorCategory.Operation
            && string.Equals(error.Subject, operationId, StringComparison.Ordinal)
            && error.Problem.Contains(expectedProblem, StringComparison.Ordinal))).IsTrue();
    }

    private static class QueryScenarioData
    {
        public static void NullableString(SchemaBuilder schema) => schema.AnyOf(
            static branch => branch.Type("string"),
            static branch => branch.Type("null"));

        public static void NullableOrderEnum(SchemaBuilder schema) => schema.AnyOf(
            static branch => branch.Type("string").Enum("asc", "desc"),
            static branch => branch.Type("null"));

        public static void NullableParentFilter(SchemaBuilder schema) => schema.AnyOf(
            static branch => branch.AnyOf(
                static inner => inner.Type("string").AllOf(static constraint => constraint.Raw("pattern", "\"^wid\"")),
                static inner => inner.Type("string").Enum("null")),
            static branch => branch.Type("null"));

        public static void NullableLocationSelector(SchemaBuilder schema) => schema.AnyOf(
            static branch => branch.Type("object")
                .AdditionalPropertiesFalse()
                .Property("directory", static property => property.AnyOf(
                    static inner => inner.Type("string"),
                    static inner => inner.Type("null")))
                .Property("workspace", static property => property.AnyOf(
                    static inner => inner.Type("string"),
                    static inner => inner.Type("null"))),
            static branch => branch.Type("null"));

        public static void NullableSelectorWithExtraMember(SchemaBuilder schema) => schema.AnyOf(
            static branch => branch.Type("object")
                .AdditionalPropertiesFalse()
                .Property("directory", static property => property.AnyOf(
                    static inner => inner.Type("string"),
                    static inner => inner.Type("null")))
                .Property("workspace", static property => property.AnyOf(
                    static inner => inner.Type("string"),
                    static inner => inner.Type("null")))
                .Property("project", static property => property.AnyOf(
                    static inner => inner.Type("string"),
                    static inner => inner.Type("null"))),
            static branch => branch.Type("null"));
    }

    private static SpecScenario GadgetScenario(Action<SpecDocumentBuilder>? extend = null, bool parametersReversed = false) =>
        SpecScenario.Define(spec =>
        {
            _ = spec
                .WithSchema("GadgetPart", schema => schema.Type("object")
                    .Property("id", property => property.Type("string"), required: true))
                .WithSchema("GadgetMissingError", schema => schema.Type("object")
                    .Property("_tag", property => property.Type("string").Enum("GadgetMissingError"), required: true)
                    .Property("message", property => property.Type("string"), required: true))
                .WithOperation("v2.gadget.part", path: "/api/gadget/{gadgetID}/part/{partID}", configure: operation =>
                {
                    if (parametersReversed)
                    {
                        _ = operation.Parameter("partID", "path", schema => schema.Type("string"), required: true)
                            .Parameter("gadgetID", "path", schema => schema.Type("string"), required: true);
                    }
                    else
                    {
                        _ = operation.Parameter("gadgetID", "path", schema => schema.Type("string"), required: true)
                            .Parameter("partID", "path", schema => schema.Type("string"), required: true);
                    }

                    _ = operation
                        .Summary("Get gadget part")
                        .Response(200, "application/json", schema => schema.Type("object")
                            .Property("data", property => property.Ref("GadgetPart"), required: true))
                        .Response(404, "application/json", schema => schema.Ref("GadgetMissingError"));
                });
            extend?.Invoke(spec);
        });

    private static SpecScenario StatusScenario() =>
        SpecScenario.Define(spec => spec
            .WithSchema("WidgetState", schema => schema.Type("object")
                .Property("value", property => property.Type("string"), required: true))
            .WithOperation("v2.widget.status", path: "/api/widget-status", configure: operation => operation
                .Response(200, "application/json", schema => schema.Ref("WidgetState"))));
}
