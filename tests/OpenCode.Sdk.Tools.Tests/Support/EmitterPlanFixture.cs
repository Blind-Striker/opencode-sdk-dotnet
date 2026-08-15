using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal static class EmitterPlanFixture
{
    public static EmitPlan Create()
    {
        var models = new ModelPlan[]
        {
            CreateExampleItem(),
            CreateExampleMode(),
            CreateVariant("CreatedEvent", "created", "item", "Item", Named("ExampleItem")),
            CreateVariant("DeletedEvent", "deleted", "id", "ID", Named("string")),
            CreateError(),
            CreateWidgetCreateRequest(),
        };
        var unions = new[]
        {
            CreateExampleEvent(),
            CreateOpenCodeError(),
        };

        return new EmitPlan
        {
            SelectedOperationIds = ["v2.example.get"],
            Models = models,
            Unions = unions,
            Clients = CreateClientPlans(),
            Registry = new RegistryPlan
            {
                TypeNames =
                [
                    "BadRequestError",
                    "CreatedEvent",
                    "DeletedEvent",
                    "ExampleEvent",
                    "ExampleItem",
                    "ExampleMode",
                    "OpenCodeError",
                    "UnknownExampleEvent",
                    "UnknownOpenCodeError",
                    "WidgetCreateRequest",
                    "WidgetCreateResponseEnvelope",
                    "WidgetItemListResponseEnvelope",
                    "WidgetItemResponseEnvelope",
                    "WidgetListResponseEnvelope",
                ],
            },
            PendingOperations = [],
        };
    }

    public static IReadOnlyList<ClientPlan> CreateClientPlans() =>
    [
        new ClientPlan
        {
            Name = "OpenCodeClient",
            Namespace = "OpenCode.Sdk",
            Role = ClientRole.Root,
            SubClients =
            [
                new ClientReferencePlan
                {
                    PropertyName = "Widgets",
                    TypeName = "WidgetsClient",
                },
            ],
            Operations = [CreatePingOperation()],
        },
        new ClientPlan
        {
            Name = "WidgetClient",
            Namespace = "OpenCode.Sdk",
            Role = ClientRole.Handle,
            ContainerName = "Widgets",
            SubClients = [],
            HandleParameter = CreateWidgetParameter(),
            Operations = [CreateItemOperation(), CreateItemListOperation()],
        },
        new ClientPlan
        {
            Name = "WidgetsClient",
            Namespace = "OpenCode.Sdk",
            Role = ClientRole.Collection,
            ContainerName = "Widgets",
            SubClients = [],
            HandleFactory = new HandleFactoryPlan
            {
                MethodName = "GetWidgetClient",
                HandleTypeName = "WidgetClient",
                Parameter = CreateWidgetParameter(),
            },
            Operations = [CreateOverviewOperation(), CreateWidgetListOperation(), CreateWidgetCreateOperation()],
        },
    ];

    private static OperationPlan CreatePingOperation() =>
        new()
        {
            MethodName = "GetPingAsync",
            HttpMethod = "get",
            RouteTemplate = "/api/ping",
            RouteContainerName = "Ping",
            RouteMemberName = "Get",
            Parameters = [],
            Envelope = new EnvelopePlan
            {
                ResponseTypeName = "PingResponse",
                AdapterTypeName = "PingResponseAdapter",
                PayloadName = "Ping",
                PayloadTypeName = "ExampleItem",
                Kind = EnvelopeKind.Bare,
            },
            ErrorMap = new ErrorMapPlan
            {
                Statuses =
                [
                    new ErrorStatusPlan
                    {
                        StatusCode = 400,
                        Tags = [new ErrorTagPlan { Tag = "BadRequestError", TypeName = "BadRequestError", }],
                    },
                ],
            },
            Summary = "Check example ping",
            Description = "Reports the example service state.",
        };

    private static OperationPlan CreateOverviewOperation() =>
        new()
        {
            MethodName = "GetOverviewAsync",
            HttpMethod = "get",
            RouteTemplate = "/api/widget-overview",
            RouteContainerName = "Widgets",
            RouteMemberName = "GetOverview",
            Parameters = [],
            Envelope = new EnvelopePlan
            {
                ResponseTypeName = "WidgetOverviewResponse",
                AdapterTypeName = "WidgetOverviewResponseAdapter",
                PayloadName = "Overview",
                PayloadTypeName = "ExampleItem",
                Kind = EnvelopeKind.Bare,
            },
            ErrorMap = new ErrorMapPlan
            {
                Statuses = [],
            },
            Summary = "List the widget overview",
            Description = null,
        };

    private static OperationPlan CreateItemOperation() =>
        new()
        {
            MethodName = "GetItemAsync",
            HttpMethod = "get",
            RouteTemplate = "/api/widget/{widgetID}/item/{itemID}",
            RouteContainerName = "Widgets",
            RouteMemberName = "GetItem",
            Parameters =
            [
                CreateWidgetParameter(),
                new OperationParameterPlan
                {
                    WireName = "itemID",
                    Name = "itemId",
                    TypeName = "string",
                    IsHandleParameter = false,
                },
            ],
            Envelope = new EnvelopePlan
            {
                ResponseTypeName = "WidgetItemResponse",
                AdapterTypeName = "WidgetItemResponseAdapter",
                PayloadName = "Item",
                PayloadTypeName = "ExampleItem",
                Kind = EnvelopeKind.Data,
                EnvelopeDtoTypeName = "WidgetItemResponseEnvelope",
            },
            ErrorMap = new ErrorMapPlan
            {
                Statuses =
                [
                    new ErrorStatusPlan
                    {
                        StatusCode = 400,
                        Tags = [new ErrorTagPlan { Tag = "BadRequestError", TypeName = "BadRequestError", }],
                    },
                    new ErrorStatusPlan
                    {
                        StatusCode = 404,
                        Tags =
                        [
                            new ErrorTagPlan { Tag = "GoneError", TypeName = "GoneError", },
                            new ErrorTagPlan { Tag = "MissingError", TypeName = "MissingError", },
                        ],
                    },
                ],
            },
            Summary = "Get one widget item",
            Description = "Retrieves one item owned by the widget.",
        };

    private static OperationParameterPlan CreateWidgetParameter() =>
        new()
        {
            WireName = "widgetID",
            Name = "widgetId",
            TypeName = "string",
            IsHandleParameter = true,
        };

    /// <summary>A flat query-request list operation covering every query value kind, a cursor envelope, and a 5xx arm.</summary>
    private static OperationPlan CreateWidgetListOperation() =>
        new()
        {
            MethodName = "ListWidgetsAsync",
            HttpMethod = "get",
            RouteTemplate = "/api/widget",
            RouteContainerName = "Widgets",
            RouteMemberName = "ListWidgets",
            Parameters = [],
            QueryRequest = new QueryRequestPlan
            {
                TypeName = "WidgetListRequest",
                DerivesFromListRequest = false,
                Properties =
                [
                    QueryProperty("limit", "Limit", QueryValueKind.PositiveCount),
                    QueryProperty("order", "Order", QueryValueKind.ListOrder),
                    QueryProperty("cursor", "Cursor", QueryValueKind.Text),
                    QueryProperty("parentID", "ParentId", QueryValueKind.SessionParentFilter),
                ],
            },
            Envelope = new EnvelopePlan
            {
                ResponseTypeName = "WidgetListResponse",
                AdapterTypeName = "WidgetListResponseAdapter",
                PayloadName = "Widgets",
                PayloadTypeName = "ExampleItem",
                Kind = EnvelopeKind.CursorList,
                EnvelopeDtoTypeName = "WidgetListResponseEnvelope",
            },
            ErrorMap = new ErrorMapPlan
            {
                Statuses =
                [
                    new ErrorStatusPlan
                    {
                        StatusCode = 400,
                        Tags = [new ErrorTagPlan { Tag = "BadRequestError", TypeName = "BadRequestError", }],
                    },
                    new ErrorStatusPlan
                    {
                        StatusCode = 500,
                        Tags = [new ErrorTagPlan { Tag = "BadRequestError", TypeName = "BadRequestError", }],
                    },
                ],
            },
            Summary = "List the widgets",
            Description = null,
        };

    /// <summary>A handle-scoped list whose query request derives from the ListRequest base.</summary>
    private static OperationPlan CreateItemListOperation() =>
        new()
        {
            MethodName = "ListItemsAsync",
            HttpMethod = "get",
            RouteTemplate = "/api/widget/{widgetID}/item",
            RouteContainerName = "Widgets",
            RouteMemberName = "ListItems",
            Parameters = [CreateWidgetParameter()],
            QueryRequest = new QueryRequestPlan
            {
                TypeName = "ItemListRequest",
                DerivesFromListRequest = true,
                Properties =
                [
                    QueryProperty("limit", "Limit", QueryValueKind.PositiveCount, isInherited: true),
                    QueryProperty("order", "Order", QueryValueKind.ListOrder, isInherited: true),
                    QueryProperty("cursor", "Cursor", QueryValueKind.Text, isInherited: true),
                ],
            },
            Envelope = new EnvelopePlan
            {
                ResponseTypeName = "WidgetItemListResponse",
                AdapterTypeName = "WidgetItemListResponseAdapter",
                PayloadName = "Items",
                PayloadTypeName = "ExampleItem",
                Kind = EnvelopeKind.CursorList,
                EnvelopeDtoTypeName = "WidgetItemListResponseEnvelope",
            },
            ErrorMap = new ErrorMapPlan
            {
                Statuses =
                [
                    new ErrorStatusPlan
                    {
                        StatusCode = 404,
                        Tags = [new ErrorTagPlan { Tag = "MissingError", TypeName = "MissingError", }],
                    },
                ],
            },
            Summary = "List the widget items",
            Description = null,
        };

    /// <summary>A create operation with an optional all-optional request body.</summary>
    private static OperationPlan CreateWidgetCreateOperation() =>
        new()
        {
            MethodName = "CreateWidgetAsync",
            HttpMethod = "post",
            RouteTemplate = "/api/widget",
            RouteContainerName = "Widgets",
            RouteMemberName = "CreateWidget",
            Parameters = [],
            RequestBody = new RequestBodyPlan
            {
                TypeName = "WidgetCreateRequest",
                ParameterName = "request",
                IsOptional = true,
            },
            Envelope = new EnvelopePlan
            {
                ResponseTypeName = "WidgetCreateResponse",
                AdapterTypeName = "WidgetCreateResponseAdapter",
                PayloadName = "Widget",
                PayloadTypeName = "ExampleItem",
                Kind = EnvelopeKind.Data,
                EnvelopeDtoTypeName = "WidgetCreateResponseEnvelope",
            },
            ErrorMap = new ErrorMapPlan
            {
                Statuses =
                [
                    new ErrorStatusPlan
                    {
                        StatusCode = 400,
                        Tags = [new ErrorTagPlan { Tag = "BadRequestError", TypeName = "BadRequestError", }],
                    },
                ],
            },
            Summary = "Create one widget",
            Description = null,
        };

    private static QueryPropertyPlan QueryProperty(string wireName, string propertyName, QueryValueKind kind,
        bool isInherited = false) =>
        new()
        {
            WireName = wireName,
            PropertyName = propertyName,
            Kind = kind,
            IsInherited = isInherited,
        };

    private static ObjectModelPlan CreateWidgetCreateRequest() =>
        new()
        {
            Name = "WidgetCreateRequest",
            Namespace = "OpenCode.Sdk.Models",
            Description = "Shapes the widget-create request body.",
            Properties =
            [
                Property("title", "Title", Named("string", isNullable: true), isRequired: false, "Gets the widget title."),
            ],
        };

    public static EmitPlan CreateModelSnapshot()
    {
        var plan = Create();
        return plan with
        {
            Models =
            [
                plan.Models.Single(static model => model.Name == "BadRequestError"),
                plan.Models.Single(static model => model.Name == "ExampleItem"),
                plan.Models.Single(static model => model.Name == "ExampleMode"),
            ],
            Unions = [plan.Unions.Single(static union => union.Name == "OpenCodeError")],
        };
    }

    public static IReadOnlyList<UnionPlan> CreateUnionSnapshot() => [CreateExampleEvent()];

    public static RegistryPlan CreateRegistry() => Create().Registry;

    private static ObjectModelPlan CreateExampleItem() =>
        new()
        {
            Name = "ExampleItem",
            Namespace = "OpenCode.Sdk.Models",
            Description = "Represents one emitted item.",
            Properties =
            [
                Property("id", "ID", Named("string"), isRequired: true, "Gets the item identifier."),
                Property("note", "Note", Named("string", isNullable: true), isRequired: false, description: null),
                Property("count", "Count", Named("double", isNullable: true), isRequired: false, "Gets the item count."),
                Property("flushedAt", "FlushedAt", Named("double", isNullable: true), isRequired: false,
                    "Gets the flush timestamp, or null when never flushed.", allowsWireNull: true),
                Property("tags", "Tags", ListOf(Named("string")), isRequired: false, "Gets the item tags."),
                Property("links", "Links", DictionaryOf(Named("Uri")), isRequired: false, "Gets links by relation."),
            ],
        };

    private static EnumModelPlan CreateExampleMode() =>
        new()
        {
            Name = "ExampleMode",
            Namespace = "OpenCode.Sdk.Models",
            Description = null,
            Values =
            [
                new EnumValuePlan { Name = "FastMode", WireValue = "fast-mode", },
                new EnumValuePlan { Name = "Safe", WireValue = "safe", },
            ],
        };

    private static ObjectModelPlan CreateVariant(string name, string tag, string wireName, string propertyName,
        TypeReferencePlan propertyType) =>
        new()
        {
            Name = name,
            Namespace = "OpenCode.Sdk.Models",
            Description = null,
            BaseTypeName = "ExampleEvent",
            Properties =
            [
                LiteralProperty("type", "Type", tag),
                Property(wireName, propertyName, propertyType, isRequired: true, description: null),
            ],
        };

    private static ObjectModelPlan CreateError() =>
        new()
        {
            Name = "BadRequestError",
            Namespace = "OpenCode.Sdk.Models",
            Description = "Represents a rejected request.",
            BaseTypeName = "OpenCodeError",
            Properties =
            [
                LiteralProperty("_tag", "Tag", "BadRequestError"),
                Property("message", "Message", Named("string"), isRequired: true, "Gets the error message."),
            ],
        };

    private static UnionPlan CreateExampleEvent() =>
        new()
        {
            Name = "ExampleEvent",
            Namespace = "OpenCode.Sdk.Models",
            UnknownTypeName = "UnknownExampleEvent",
            MarkerWireName = "type",
            MarkerName = "Type",
            MarkerKind = LiteralKind.String,
            Variants =
            [
                new UnionVariantPlan { TypeName = "CreatedEvent", Tag = "created", },
                new UnionVariantPlan { TypeName = "DeletedEvent", Tag = "deleted", },
            ],
            Description = "Represents an example event.",
        };

    private static UnionPlan CreateOpenCodeError() =>
        new()
        {
            Name = "OpenCodeError",
            Namespace = "OpenCode.Sdk.Models",
            UnknownTypeName = "UnknownOpenCodeError",
            MarkerWireName = "_tag",
            MarkerName = "Tag",
            MarkerKind = LiteralKind.String,
            Variants = [new UnionVariantPlan { TypeName = "BadRequestError", Tag = "BadRequestError", }],
            Description = "Represents an opencode API error.",
        };

    private static ModelPropertyPlan Property(string wireName, string name, TypeReferencePlan type, bool isRequired,
        string? description, bool allowsWireNull = false) =>
        new()
        {
            WireName = wireName,
            Name = name,
            Type = type,
            IsRequired = isRequired,
            AllowsWireNull = allowsWireNull,
            IsLiteral = false,
            Description = description,
        };

    private static ModelPropertyPlan LiteralProperty(string wireName, string name, string value) =>
        new()
        {
            WireName = wireName,
            Name = name,
            Type = Named("string"),
            IsRequired = true,
            AllowsWireNull = false,
            IsLiteral = true,
            LiteralKind = LiteralKind.String,
            LiteralValue = value,
            Description = null,
        };

    private static NamedTypeReferencePlan Named(string name, bool isNullable = false) =>
        new()
        {
            Name = name,
            IsNullable = isNullable,
        };

    private static ListTypeReferencePlan ListOf(TypeReferencePlan elementType) =>
        new()
        {
            ElementType = elementType,
            IsNullable = false,
        };

    private static DictionaryTypeReferencePlan DictionaryOf(TypeReferencePlan valueType) =>
        new()
        {
            ValueType = valueType,
            IsNullable = false,
        };
}
