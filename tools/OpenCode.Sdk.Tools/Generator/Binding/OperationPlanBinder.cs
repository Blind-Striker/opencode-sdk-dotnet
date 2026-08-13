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
    /// Member names every generated response envelope inherits from the response spine; a
    /// payload landing on one of them needs a curated override.
    /// </summary>
    private static readonly string[] ReservedPayloadNames = ["Error", "IsError", "RawBody", "Status"];

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
        CheckTypeNameCollisions(clients, bound, errors);
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
        foreach (var group in bound
                     .Where(static operation => operation.Row.Placement is GroupPlacement.Client)
                     .GroupBy(static operation => operation.Group, _comparer)
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

    private void CheckTypeNameCollisions(List<ClientPlan> clients, List<BoundOperation> bound, BindingErrorCollector errors)
    {
        var owners = new HashSet<string>(_comparer);
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
            if (envelope is null || errorMap is null || parameters is null)
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
                    MethodName = OperationNamePolicy.MethodName(_operation, row.Placement),
                    HttpMethod = _operation.Method,
                    RouteTemplate = _operation.Path,
                    RouteContainerName = row.ClientName ?? CSharpNamePolicy.ToPascalCase(group),
                    RouteMemberName = OperationNamePolicy.RouteMemberName(_operation),
                    Parameters = parameters,
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
            if (!string.Equals(_operation.Method, "get", StringComparison.Ordinal))
            {
                Refuse("only GET operations are supported in M1");
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

            if (_operation.RequestBody is not null)
            {
                Refuse("request bodies are not supported in M1");
            }

            CheckParameterShapes();
            CheckStatusShape();
            return _errors.Count == before;
        }

        private void CheckParameterShapes()
        {
            foreach (var parameter in _operation.Parameters)
            {
                if (parameter is not { Location: SpecParameterLocation.Path, IsRequired: true, IsDeepObject: false })
                {
                    Refuse($"parameter '{parameter.Name}' must be a required path parameter");
                    continue;
                }

                if (Resolve(parameter.Schema) is not PrimitiveNode { Kind: PrimitiveKind.String })
                {
                    Refuse($"parameter '{parameter.Name}' must be a plain string");
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
                SpecEnvelopeShape.None or SpecEnvelopeShape.DataLocation or SpecEnvelopeShape.CursorData
                    or SpecEnvelopeShape.DataHasMore
                    => RefuseNull($"envelope shape '{success.EnvelopeShape}' is not supported in M1"),
                _ => RefuseNull($"envelope shape '{success.EnvelopeShape}' is not supported in M1"),
            };
            if (payload is null)
            {
                return null;
            }

            var responseTypeName = OperationNamePolicy.ResponseTypeName(_operation);
            var payloadName = _curation.EnvelopePayloadNames.TryGetValue(_operation.OperationId, out var curated)
                ? curated
                : OperationNamePolicy.PayloadName(_operation);
            if (ReservedPayloadNames.Contains(payloadName, StringComparer.Ordinal)
                || string.Equals(payloadName, responseTypeName, StringComparison.Ordinal))
            {
                _errors.Add(
                    BindingErrorCategory.Naming,
                    _operation.OperationId,
                    $"payload name '{payloadName}' collides with the response spine of '{responseTypeName}'");
                return null;
            }

            return new EnvelopePlan
            {
                ResponseTypeName = responseTypeName,
                AdapterTypeName = $"{responseTypeName}Adapter",
                PayloadName = payloadName,
                PayloadTypeName = payload,
                HasDataEnvelope = success.EnvelopeShape is SpecEnvelopeShape.Data,
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

            return [.. plans.OrderBy(plan => TemplatePosition(plan.WireName))];
        }

        private int TemplatePosition(string wireName)
        {
            var position = _operation.Path.IndexOf($"{{{wireName}}}", StringComparison.Ordinal);
            Debug.Assert(position >= 0, "Ingestion guarantees every path parameter appears in the route template.");
            return position;
        }

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
