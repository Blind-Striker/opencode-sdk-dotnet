using System.Diagnostics;
using System.Globalization;
using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class OperationPlanBinder
{
    private const string ClientNamespace = "OpenCode.Sdk";
    private const string RootClientName = "OpenCodeClient";

    /// <summary>
    /// Member names every generated response envelope inherits from the response spine or
    /// from <see cref="object"/>/record synthesis; a payload landing on one of them needs a
    /// curated override.
    /// </summary>
    /// <summary>Hand-written public spine types in the client namespace; a generated twin would not compile.</summary>
    private static readonly string[] ReservedSpineTypeNames =
    [
        "ErrorBehavior",
        "IOpenCodeClientOptions",
        "ListCursor",
        "ListOrder",
        "ListRequest",
        "OpenCodeApiException",
        "OpenCodeClientOptions",
        "OpenCodeException",
        "OpenCodeRequestOptions",
        "OpenCodeResponse",
        "OpenCodeTransportException",
        "SessionParentFilter",
    ];

    /// <summary>Parameter names the emitted method and route-builder signatures append themselves.</summary>
    private static readonly string[] ReservedParameterNames =
    [
        "cancellationToken",
        "pipeline",
        "request",
        "requestOptions",
    ];

    private static readonly string[] ReservedPayloadNames =
    [
        "Cursor",
        "Deconstruct",
        "EqualityContract",
        "Equals",
        "Error",
        "GetHashCode",
        "GetType",
        "IsError",
        "MemberwiseClone",
        "PrintMembers",
        "RawBody",
        "ReferenceEquals",
        "Status",
        "ToString",
    ];

    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public IReadOnlyList<ClientPlan> Bind(SpecDocument document, IReadOnlyList<SpecOperation> selected,
        GenerationCuration curation, IReadOnlyDictionary<string, string> typeNames, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(errors);

        var bound = new List<BoundOperation>(selected.Count);
        foreach (var operation in selected.OrderBy(static operation => operation.OperationId, _comparer))
        {
            var operationPlan = new SingleOperationBinder(document, operation, curation, typeNames, errors).Bind();
            if (operationPlan is not null)
            {
                bound.Add(operationPlan);
            }
        }

        if (bound.Count is 0)
        {
            return [];
        }

        var clients = AssembleClients(bound);
        CheckMemberCollisions(clients, errors);
        CheckTypeNameCollisions(clients, bound, typeNames, errors);
        return clients;
    }

    private List<ClientPlan> AssembleClients(List<BoundOperation> bound)
    {
        var rootOperations = bound
            .Where(static operation => operation.Row.Placement is GroupPlacement.Root)
            .Select(static operation => operation.Plan)
            .OrderBy(static plan => plan.MethodName, _comparer)
            .ToArray();
        var subClients = new List<ClientReferencePlan>();
        var clients = new List<ClientPlan>();

        // Wire groups sharing a curated client name merge into one client family; the
        // curation validator has already demanded identical handle declarations.
        foreach (var group in bound
                     .Where(static operation => operation.Row.Placement is GroupPlacement.Client)
                     .GroupBy(static operation => operation.Row.ClientName!, _comparer)
                     .OrderBy(static group => group.Key, _comparer))
        {
            var members = group.ToArray();
            var row = members[0].Row;
            var collectionName = $"{row.ClientName}Client";
            subClients.Add(new ClientReferencePlan
            {
                PropertyName = row.ClientName!,
                TypeName = collectionName,
            });
            clients.AddRange(CreateGroupClients(members, row, collectionName));
        }

        clients.Add(new ClientPlan
        {
            Name = RootClientName,
            Namespace = ClientNamespace,
            Role = ClientRole.Root,
            SubClients = [.. subClients.OrderBy(static reference => reference.PropertyName, _comparer)],
            Operations = rootOperations,
        });

        return
        [
            .. clients.OrderBy(static client => client.Role is ClientRole.Root ? 0 : 1)
                .ThenBy(static client => client.Name, StringComparer.Ordinal),
        ];
    }

    private IEnumerable<ClientPlan> CreateGroupClients(IReadOnlyList<BoundOperation> group, GroupCuration row, string collectionName)
    {
        var handleOperations = group.Where(static operation => operation.IsHandleOperation).ToArray();
        var collectionOperations = group
            .Where(static operation => !operation.IsHandleOperation)
            .Select(static operation => operation.Plan)
            .OrderBy(static plan => plan.MethodName, _comparer)
            .ToArray();
        var handleParameter = handleOperations
            .OrderBy(static operation => operation.Plan.MethodName, _comparer)
            .SelectMany(static operation => operation.Plan.Parameters)
            .FirstOrDefault(static parameter => parameter.IsHandleParameter);

        yield return new ClientPlan
        {
            Name = collectionName,
            Namespace = ClientNamespace,
            Role = ClientRole.Collection,
            ContainerName = row.ClientName,
            SubClients = [],
            HandleFactory = handleParameter is null ? null : new HandleFactoryPlan
            {
                MethodName = $"Get{row.HandleName}",
                HandleTypeName = row.HandleName!,
                Parameter = handleParameter,
            },
            Operations = collectionOperations,
        };

        if (handleParameter is not null)
        {
            yield return new ClientPlan
            {
                Name = row.HandleName!,
                Namespace = ClientNamespace,
                Role = ClientRole.Handle,
                ContainerName = row.ClientName,
                SubClients = [],
                HandleParameter = handleParameter,
                Operations =
                [
                    .. handleOperations
                        .Select(static operation => operation.Plan)
                        .OrderBy(static plan => plan.MethodName, _comparer),
                ],
            };
        }
    }

    private void CheckMemberCollisions(List<ClientPlan> clients, BindingErrorCollector errors)
    {
        foreach (var client in clients)
        {
            var members = client.Operations.Select(static operation => operation.MethodName)
                .Concat(client.SubClients.Select(static reference => reference.PropertyName));
            if (client.HandleFactory is not null)
            {
                members = members.Append(client.HandleFactory.MethodName);
            }

            foreach (var member in members.GroupBy(static member => member, _comparer)
                         .Where(static member => member.Skip(1).Any())
                         .OrderBy(static member => member.Key, _comparer))
            {
                errors.Add(BindingErrorCategory.Naming, client.Name, $"multiple members map to C# name '{member.Key}'");
            }
        }
    }

    private void CheckTypeNameCollisions(List<ClientPlan> clients, List<BoundOperation> bound,
        IReadOnlyDictionary<string, string> typeNames, BindingErrorCollector errors)
    {
        // Model names shadow across namespaces (consumer CS0104) and hand-written spine
        // names collide outright (CS0101), so both seed the owner set.
        var owners = new HashSet<string>(_comparer);
        owners.UnionWith(typeNames.Values);
        owners.UnionWith(ReservedSpineTypeNames);
        foreach (var name in clients.Select(static client => client.Name).Where(name => !owners.Add(name)))
        {
            errors.Add(BindingErrorCategory.Naming, name, $"client type name '{name}' collides with another generated type");
        }

        foreach (var operation in bound.Where(operation => !owners.Add(operation.Plan.Envelope.ResponseTypeName)))
        {
            errors.Add(
                BindingErrorCategory.Naming,
                operation.OperationId,
                $"multiple operations map to response type '{operation.Plan.Envelope.ResponseTypeName}'");
        }

        foreach (var operation in bound.Where(operation =>
                     operation.Plan.QueryRequest is not null && !owners.Add(operation.Plan.QueryRequest.TypeName)))
        {
            errors.Add(
                BindingErrorCategory.Naming,
                operation.OperationId,
                $"request type name '{operation.Plan.QueryRequest!.TypeName}' collides with another generated type");
        }

        var routeMembers = new HashSet<string>(_comparer);
        foreach (var operation in bound.Where(operation =>
                     !routeMembers.Add($"{operation.Plan.RouteContainerName}.{operation.Plan.RouteMemberName}")))
        {
            errors.Add(
                BindingErrorCategory.Naming,
                operation.OperationId,
                $"multiple operations map to route member '{operation.Plan.RouteContainerName}.{operation.Plan.RouteMemberName}'");
        }
    }

    private sealed record BoundOperation
    {
        public required string OperationId { get; init; }

        public required string Group { get; init; }

        public required GroupCuration Row { get; init; }

        public required bool IsHandleOperation { get; init; }

        public required OperationPlan Plan { get; init; }
    }

    /// <summary>Binds one selected operation, batching every wire-contract refusal it finds.</summary>
    private sealed class SingleOperationBinder(SpecDocument document, SpecOperation operation,
        GenerationCuration curation, IReadOnlyDictionary<string, string> typeNames, BindingErrorCollector errors)
    {
        private readonly GenerationCuration _curation = curation;
        private readonly SpecDocument _document = document;
        private readonly BindingErrorCollector _errors = errors;
        private readonly SpecOperation _operation = operation;
        private readonly IReadOnlyDictionary<string, string> _typeNames = typeNames;

        public BoundOperation? Bind()
        {
            var group = _operation.Segments[0];
            if (!_curation.Groups.TryGetValue(group, out var row))
            {
                // The curation validator already reported the missing row.
                return null;
            }

            if (!CheckWireShape())
            {
                return null;
            }

            var success = _operation.Responses.Single(static response => response.StatusCode is 200);
            var envelope = BindEnvelope(success);
            var errorMap = BindErrorMap();
            var parameters = BindParameters(row);
            var optionalPlanErrorsBefore = _errors.Count;
            var queryRequest = BindQueryRequest();
            var requestBody = BindRequestBody();
            var methodName = OperationNamePolicy.MethodName(_operation);
            var routeMemberName = OperationNamePolicy.RouteMemberName(_operation, row.Placement);
            if (methodName is null || routeMemberName is null)
            {
                Refuse("the operation's names cannot be derived mechanically: the group does not pluralize naively");
                return null;
            }

            if (envelope is null || errorMap is null || parameters is null || _errors.Count != optionalPlanErrorsBefore)
            {
                return null;
            }

            return new BoundOperation
            {
                OperationId = _operation.OperationId,
                Group = group,
                Row = row,
                IsHandleOperation = parameters.Any(static parameter => parameter.IsHandleParameter),
                Plan = new OperationPlan
                {
                    MethodName = methodName,
                    HttpMethod = _operation.Method,
                    RouteTemplate = _operation.Path,
                    RouteContainerName = row.ClientName ?? CSharpNamePolicy.ToPascalCase(group),
                    RouteMemberName = routeMemberName,
                    Parameters = parameters,
                    QueryRequest = queryRequest,
                    RequestBody = requestBody,
                    Envelope = envelope,
                    ErrorMap = errorMap,
                    Summary = _operation.Summary,
                    Description = _operation.Description,
                },
            };
        }

        private bool CheckWireShape()
        {
            var before = _errors.Count;
            var isGet = string.Equals(_operation.Method, "get", StringComparison.Ordinal);
            var isPost = string.Equals(_operation.Method, "post", StringComparison.Ordinal);
            if (!isGet && !isPost)
            {
                Refuse($"HTTP method '{_operation.Method}' is not supported");
            }

            if (_operation.HasWildcardPath)
            {
                Refuse("wildcard paths are not supported in M1");
            }

            if (_operation.IsWebSocket)
            {
                Refuse("WebSocket operations are not supported in M1");
            }

            if (_operation.IsSse)
            {
                Refuse("event-stream responses are not supported in M1");
            }

            if (isGet && _operation.RequestBody is not null)
            {
                Refuse("GET operations must not carry a request body");
            }

            if (isPost && _operation.RequestBody is null)
            {
                Refuse("POST operations must carry a request body");
            }

            CheckParameterShapes();
            CheckStatusShape();
            return _errors.Count == before;
        }

        private void CheckParameterShapes()
        {
            foreach (var parameter in _operation.Parameters.Where(static parameter => parameter.Location is SpecParameterLocation.Path))
            {
                if (parameter is not { IsRequired: true, IsDeepObject: false })
                {
                    Refuse($"path parameter '{parameter.Name}' must be required and plainly encoded");
                    continue;
                }

                if (Resolve(parameter.Schema) is not PrimitiveNode { Kind: PrimitiveKind.String })
                {
                    Refuse($"path parameter '{parameter.Name}' must be a plain string");
                }
            }
        }

        private void CheckStatusShape()
        {
            var successes = _operation.Responses.Where(static response => response.StatusCode is >= 200 and < 300).ToArray();
            if (successes.Length is not 1)
            {
                Refuse("operation must declare exactly one success response");
            }
            else if (successes[0].StatusCode is not 200)
            {
                Refuse("the success response must use status 200");
            }

            foreach (var response in _operation.Responses
                         .Where(static response => response.StatusCode is < 200 or (>= 300 and < 400)))
            {
                Refuse($"status '{response.StatusCode.ToString(CultureInfo.InvariantCulture)}' must be an error status");
            }
        }

        private EnvelopePlan? BindEnvelope(SpecResponse success)
        {
            if (success.ContentType is not { IsJson: true } || success.Schema is null)
            {
                Refuse("the success response must carry a JSON schema");
                return null;
            }

            var payload = success.EnvelopeShape switch
            {
                SpecEnvelopeShape.Bare => BindBarePayload(success.Schema),
                SpecEnvelopeShape.Data => BindDataPayload(success.Schema),
                SpecEnvelopeShape.CursorData => BindCursorListPayload(success.Schema),
                SpecEnvelopeShape.None or SpecEnvelopeShape.DataLocation or SpecEnvelopeShape.DataHasMore or _ =>
                    RefuseNull($"envelope shape '{success.EnvelopeShape}' is not supported"),
            };
            if (payload is null)
            {
                return null;
            }

            var responseTypeName = OperationNamePolicy.ResponseTypeName(_operation);
            var payloadName = _curation.EnvelopePayloadNames.TryGetValue(_operation.OperationId, out var curated)
                ? curated
                : OperationNamePolicy.PayloadName(_operation);
            if (payloadName is null)
            {
                Refuse("the payload name cannot be derived mechanically: the group does not pluralize naively; curate an envelope payload name");
                return null;
            }

            // Mechanical names are PascalCase by construction, but the wall covers both origins.
            if (!CSharpNamePolicy.IsValidIdentifier(payloadName))
            {
                _errors.Add(
                    BindingErrorCategory.Naming,
                    _operation.OperationId,
                    $"payload name '{payloadName}' is not a valid C# identifier");
                return null;
            }

            if (ReservedPayloadNames.Contains(payloadName, StringComparer.Ordinal)
                || string.Equals(payloadName, responseTypeName, StringComparison.Ordinal))
            {
                _errors.Add(
                    BindingErrorCategory.Naming,
                    _operation.OperationId,
                    $"payload name '{payloadName}' collides with the response spine of '{responseTypeName}'");
                return null;
            }

            var kind = success.EnvelopeShape switch
            {
                SpecEnvelopeShape.Data => EnvelopeKind.Data,
                SpecEnvelopeShape.CursorData => EnvelopeKind.CursorList,
                SpecEnvelopeShape.Bare or SpecEnvelopeShape.None or SpecEnvelopeShape.DataLocation
                    or SpecEnvelopeShape.DataHasMore or _ => EnvelopeKind.Bare,
            };
            return new EnvelopePlan
            {
                ResponseTypeName = responseTypeName,
                AdapterTypeName = $"{responseTypeName}Adapter",
                PayloadName = payloadName,
                PayloadTypeName = payload,
                Kind = kind,
                EnvelopeDtoTypeName = kind is EnvelopeKind.Bare ? null : $"{responseTypeName}Envelope",
            };
        }

        private string? BindBarePayload(SchemaNode schema)
        {
            return schema is RefNode reference && _typeNames.TryGetValue(reference.Target, out var name)
                ? name
                : RefuseNull("success payload must reference a named schema");
        }

        private string? BindDataPayload(SchemaNode schema)
        {
            if (schema is RefNode reference
                && _document.Schemas.TryGetValue(reference.Target, out var target)
                && target is ObjectNode { Properties: [{ Name: "data", IsRequired: true } data] }
                && data.Schema is RefNode payload
                && _typeNames.TryGetValue(payload.Target, out var name))
            {
                return name;
            }

            return RefuseNull("envelope payload must be a required reference to a named schema");
        }

        private string? BindCursorListPayload(SchemaNode schema)
        {
            if (schema is not RefNode reference
                || !_document.Schemas.TryGetValue(reference.Target, out var target)
                || target is not ObjectNode wrapper)
            {
                return RefuseNull("cursor-list envelope must reference an object schema");
            }

            var data = wrapper.Properties.FirstOrDefault(static property => property.Name is "data");
            var cursor = wrapper.Properties.FirstOrDefault(static property => property.Name is "cursor");
            if (wrapper.Properties.Count is not 2 || data is not { IsRequired: true } || cursor is not { IsRequired: true })
            {
                return RefuseNull("cursor-list envelope must require exactly 'data' and 'cursor'");
            }

            if (!IsListCursorShape(cursor.Schema))
            {
                return RefuseNull("cursor-list 'cursor' must be the optional-nullable previous/next cursor object");
            }

            // Items must reference top-level components: a promoted inline item would take its
            // name from the excluded response root, so the dialect keeps list items nominal.
            if (data.Schema is not ArrayNode { Item: RefNode item }
                || item.Target.Contains('#', StringComparison.Ordinal)
                || !_typeNames.TryGetValue(item.Target, out var itemName))
            {
                return RefuseNull("cursor-list 'data' must be an array of a named component schema");
            }

            return itemName;
        }

        /// <summary>Recognizes the wire cursor contract: exactly optional-nullable string <c>previous</c> and <c>next</c>.</summary>
        private bool IsListCursorShape(SchemaNode schema)
        {
            if (Resolve(schema) is not ObjectNode cursor
                || cursor.Properties.Count is not 2
                || cursor.Properties.Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count() is not 2)
            {
                return false;
            }

            return cursor.Properties.All(property => property is { IsRequired: false, Name: "previous" or "next" }
                && Resolve(property.Schema) is NullableNode { Inner: PrimitiveNode { Kind: PrimitiveKind.String } });
        }

        private ErrorMapPlan? BindErrorMap()
        {
            var statuses = new List<ErrorStatusPlan>();
            var complete = true;
            foreach (var response in _operation.Responses.Where(static response => response.StatusCode is >= 400))
            {
                var tags = BindErrorStatus(response);
                if (tags is null)
                {
                    complete = false;
                    continue;
                }

                statuses.Add(new ErrorStatusPlan
                {
                    StatusCode = response.StatusCode,
                    Tags = tags,
                });
            }

            return complete
                ? new ErrorMapPlan
                {
                    Statuses = [.. statuses.OrderBy(static status => status.StatusCode)],
                }
                : null;
        }

        private IReadOnlyList<ErrorTagPlan>? BindErrorStatus(SpecResponse response)
        {
            if (response.ContentType is not { IsJson: true } || response.Schema is null)
            {
                return RefuseNullTags("error responses must carry a JSON schema");
            }

            var targets = ResolveErrorTargets(response.Schema);
            if (targets is null)
            {
                return RefuseNullTags("error responses must reference Effect-tagged error schemas");
            }

            var tags = new List<ErrorTagPlan>(targets.Count);
            foreach (var (key, node) in targets)
            {
                var markers = node.LiteralMarkers.Where(static marker => marker.PropertyName is "_tag").ToArray();
                if (node.ErrorStyle is not ErrorStyle.EffectTag || markers is not [var marker])
                {
                    return RefuseNullTags("error responses must reference Effect-tagged error schemas");
                }

                if (!_typeNames.TryGetValue(key, out var typeName))
                {
                    _errors.Add(BindingErrorCategory.Naming, key, "error schema has no unique C# type name");
                    return null;
                }

                tags.Add(new ErrorTagPlan
                {
                    Tag = marker.Value,
                    TypeName = typeName,
                });
            }

            var duplicate = tags.GroupBy(static tag => tag.Tag, StringComparer.Ordinal).FirstOrDefault(static tag => tag.Skip(1).Any());
            if (duplicate is not null)
            {
                return RefuseNullTags($"status '{response.StatusCode.ToString(CultureInfo.InvariantCulture)}' declares duplicate error tag '{duplicate.Key}'");
            }

            return [.. tags.OrderBy(static tag => tag.Tag, StringComparer.Ordinal)];
        }

        /// <summary>Resolves an error response schema into the object schemas carrying its tags.</summary>
        private List<KeyValuePair<string, ObjectNode>>? ResolveErrorTargets(SchemaNode schema)
        {
            if (schema is not RefNode reference || !_document.Schemas.TryGetValue(reference.Target, out var target))
            {
                return null;
            }

            switch (target)
            {
                case ObjectNode objectNode:
                    return [new KeyValuePair<string, ObjectNode>(reference.Target, objectNode)];
                case UnionNode { Classification: UnionClassification.Marked } union:
                    var branches = new List<KeyValuePair<string, ObjectNode>>(union.Branches.Count);
                    foreach (var branch in union.Branches)
                    {
                        if (branch is not RefNode branchReference
                            || !_document.Schemas.TryGetValue(branchReference.Target, out var branchTarget)
                            || branchTarget is not ObjectNode branchNode)
                        {
                            return null;
                        }

                        branches.Add(new KeyValuePair<string, ObjectNode>(branchReference.Target, branchNode));
                    }

                    return branches;
                default:
                    return null;
            }
        }

        private IReadOnlyList<OperationParameterPlan>? BindParameters(GroupCuration row)
        {
            var plans = _operation.Parameters
                .Where(static parameter => parameter.Location is SpecParameterLocation.Path)
                .Select(parameter => new OperationParameterPlan
                {
                    WireName = parameter.Name,
                    Name = CSharpNamePolicy.ToCamelCase(parameter.Name),
                    TypeName = "string",
                    IsHandleParameter = row.Placement is GroupPlacement.Client
                        && string.Equals(parameter.Name, row.HandleParameter, StringComparison.Ordinal),
                })
                .ToList();

            var duplicate = plans.GroupBy(static plan => plan.Name, StringComparer.Ordinal).FirstOrDefault(static plan => plan.Skip(1).Any());
            if (duplicate is not null)
            {
                _errors.Add(
                    BindingErrorCategory.Naming,
                    _operation.OperationId,
                    $"multiple parameters map to C# name '{duplicate.Key}'");
                return null;
            }

            // Emitted signatures append these names after bind-time checks; a wire parameter
            // landing on one must fail here, never as an emitted compile error.
            var reserved = plans
                .Select(static plan => plan.Name)
                .Where(static name => ReservedParameterNames.Contains(name, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (reserved.Length > 0)
            {
                foreach (var name in reserved)
                {
                    _errors.Add(
                        BindingErrorCategory.Naming,
                        _operation.OperationId,
                        $"parameter name '{name}' is reserved by the emitted method signature");
                }

                return null;
            }

            return [.. plans.OrderBy(plan => TemplatePosition(plan.WireName))];
        }

        private int TemplatePosition(string wireName)
        {
            var position = _operation.Path.IndexOf($"{{{wireName}}}", StringComparison.Ordinal);
            Debug.Assert(position >= 0, "Ingestion guarantees every path parameter appears in the route template.");
            return position;
        }

        private QueryRequestPlan? BindQueryRequest()
        {
            var query = _operation.Parameters
                .Where(static parameter => parameter.Location is SpecParameterLocation.Query)
                .ToArray();
            if (query.Length is 0)
            {
                return null;
            }

            var properties = new List<QueryPropertyPlan>(query.Length);
            foreach (var parameter in query)
            {
                if (parameter.IsDeepObject)
                {
                    Refuse($"query parameter '{parameter.Name}' must not use deep-object encoding");
                    continue;
                }

                if (parameter.IsRequired)
                {
                    Refuse($"query parameter '{parameter.Name}' must be optional");
                    continue;
                }

                if (Resolve(parameter.Schema) is not NullableNode nullable)
                {
                    Refuse($"query parameter '{parameter.Name}' must admit null");
                    continue;
                }

                var kind = ResolveQueryValueKind(parameter.Name, nullable.Inner);
                if (kind is null)
                {
                    Refuse($"query parameter '{parameter.Name}' has an unsupported schema shape");
                    continue;
                }

                properties.Add(new QueryPropertyPlan
                {
                    WireName = parameter.Name,
                    PropertyName = CSharpNamePolicy.ToPascalCase(parameter.Name),
                    Kind = kind.Value,
                    IsInherited = false,
                });
            }

            var duplicate = properties.GroupBy(static property => property.PropertyName, StringComparer.Ordinal)
                .FirstOrDefault(static property => property.Skip(1).Any());
            if (duplicate is not null)
            {
                _errors.Add(
                    BindingErrorCategory.Naming,
                    _operation.OperationId,
                    $"multiple query parameters map to C# name '{duplicate.Key}'");
                return null;
            }

            if (MatchesListRequestProfile(properties))
            {
                properties = [.. properties.Select(static property => property with { IsInherited = true })];
            }

            return new QueryRequestPlan
            {
                TypeName = OperationNamePolicy.RequestTypeName(_operation),
                DerivesFromListRequest = properties.Count > 0 && properties[0].IsInherited,
                Properties = properties,
            };
        }

        private RequestBodyPlan? BindRequestBody()
        {
            var body = _operation.RequestBody;
            if (body is null)
            {
                return null;
            }

            if (body.ContentType is not { IsJson: true })
            {
                Refuse("the request body must carry a JSON schema");
                return null;
            }

            if (!body.IsRequired)
            {
                Refuse("the request body must be declared required");
                return null;
            }

            if (body.Schema is not RefNode reference || Resolve(body.Schema) is not ObjectNode target)
            {
                Refuse("the request body must reference an object schema");
                return null;
            }

            if (!_typeNames.TryGetValue(reference.Target, out var typeName))
            {
                _errors.Add(BindingErrorCategory.Naming, reference.Target, "request body schema has no unique C# type name");
                return null;
            }

            return new RequestBodyPlan
            {
                TypeName = typeName,
                ParameterName = "request",
                IsOptional = target.Properties.All(static property => !property.IsRequired),
            };
        }

        /// <summary>
        /// The fail-closed profile wall: an operation derives from the <c>ListRequest</c> base
        /// only when its wire query parameters are exactly the cursor-pagination trio.
        /// </summary>
        private static bool MatchesListRequestProfile(List<QueryPropertyPlan> properties) =>
            properties.Count is 3
            && properties.Any(static property => property is { WireName: "limit", Kind: QueryValueKind.PositiveCount })
            && properties.Any(static property => property is { WireName: "order", Kind: QueryValueKind.ListOrder })
            && properties.Any(static property => property is { WireName: "cursor", Kind: QueryValueKind.Text });

        private QueryValueKind? ResolveQueryValueKind(string wireName, SchemaNode inner)
        {
            return Resolve(inner) switch
            {
                PrimitiveNode { Kind: PrimitiveKind.String } =>
                    string.Equals(wireName, "limit", StringComparison.Ordinal) ? QueryValueKind.PositiveCount : QueryValueKind.Text,
                EnumNode { Values: ["asc", "desc"] } => QueryValueKind.ListOrder,
                UnionNode { Classification: UnionClassification.Structural, Branches: [var first, var second] }
                    when IsParentFilterShape(first, second) => QueryValueKind.SessionParentFilter,
                _ => null,
            };
        }

        /// <summary>
        /// Recognizes the parent-filter wire shape — a patterned identifier string beside the
        /// literal <c>"null"</c> — structurally, never by parameter name.
        /// </summary>
        private bool IsParentFilterShape(SchemaNode first, SchemaNode second)
        {
            var left = Resolve(first);
            var right = Resolve(second);
            return (IsIdentifierString(left) && IsNullLiteral(right))
                   || (IsIdentifierString(right) && IsNullLiteral(left));
        }

        private static bool IsIdentifierString(SchemaNode schema) => schema is PrimitiveNode { Kind: PrimitiveKind.String };

        private static bool IsNullLiteral(SchemaNode schema) => schema is LiteralNode { Kind: LiteralKind.String, Value: "null" };

        private void Refuse(string problem) => _errors.Add(BindingErrorCategory.Operation, _operation.OperationId, problem);

        private string? RefuseNull(string problem)
        {
            Refuse(problem);
            return null;
        }

        private IReadOnlyList<ErrorTagPlan>? RefuseNullTags(string problem)
        {
            Refuse(problem);
            return null;
        }

        private SchemaNode Resolve(SchemaNode schema)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = schema;
            while (current is RefNode reference && visited.Add(reference.Target)
                   && _document.Schemas.TryGetValue(reference.Target, out var target))
            {
                current = target;
            }

            return current;
        }
    }
}
