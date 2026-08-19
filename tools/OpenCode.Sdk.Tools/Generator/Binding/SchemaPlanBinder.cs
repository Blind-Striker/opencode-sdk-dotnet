using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SchemaPlanBinder(
    StructuralUnionPlanBinder structuralUnions,
    UnionMembershipValidator unionMemberships)
{
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    private readonly StructuralUnionPlanBinder _structuralUnions = structuralUnions
                                                                   ?? throw new ArgumentNullException(nameof(structuralUnions));

    private readonly UnionMembershipValidator _unionMemberships = unionMemberships
                                                                  ?? throw new ArgumentNullException(nameof(unionMemberships));

    public SchemaBindingResult Bind(SpecDocument document, ReachableSchemaSet reachable, GenerationCuration curation,
        IReadOnlyDictionary<string, string> typeNames, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(errors);

        var responseRoots = reachable.ResponseRootKeys.ToHashSet(_comparer);
        var streamCauseKeys = reachable.StreamCauseKeys.ToHashSet(_comparer);
        var inhabitation = new SchemaInhabitationPolicy(document.Schemas);
        var inheritance = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unions = BindExplicitUnions(document, reachable, responseRoots, streamCauseKeys, typeNames, inheritance, inhabitation, errors);
        var errorUnion = BindErrorUnion(document, reachable, responseRoots, streamCauseKeys, typeNames, inheritance, errors);
        if (errorUnion is not null)
        {
            unions.Add(errorUnion);
        }

        var typeBinder = new TypePlanBinder(document.Schemas, typeNames, errors);
        var models = new List<ModelPlan>();
        foreach (var key in reachable.GraphKeys)
        {
            if (responseRoots.Contains(key) || !document.Schemas.TryGetValue(key, out var schema) || !typeNames.TryGetValue(key, out var name))
            {
                continue;
            }

            if (!inhabitation.IsInhabited(schema))
            {
                continue;
            }

            switch (schema)
            {
                case ObjectNode objectNode:
                    var objectPlan = BindObject(key, name, objectNode, inheritance, typeBinder, errors);
                    if (objectPlan is not null)
                    {
                        models.Add(objectPlan);
                    }

                    break;
                case EnumNode enumNode:
                    models.Add(BindEnum(name, enumNode, errors));
                    break;
                case UnionNode { Classification: UnionClassification.Structural } structural
                    when UnstructuredUnionPolicy.Collapse(structural, document.Schemas) is null:
                    var structuralPlan = _structuralUnions.Bind(key, name, structural, document.Schemas, typeBinder, errors);
                    if (structuralPlan is not null)
                    {
                        models.Add(structuralPlan);
                    }

                    break;
            }
        }

        var orderedModels = models.OrderBy(static model => model.Name, _comparer).ToArray();
        var orderedUnions = unions.OrderBy(static union => union.Name, _comparer).ToArray();
        _unionMemberships.Validate([.. orderedModels.OfType<ObjectModelPlan>()], orderedUnions, errors);
        var registryNames = orderedModels
            .Select(static model => model.Name)
            .Concat(orderedModels
                .OfType<StructuralUnionModelPlan>()
                .SelectMany(static model => model.Arms)
                .Where(static arm => arm.Type.IsCollection)
                .Select(static arm => TypeReferenceNamePolicy.Format(arm.Type)))
            .Concat(orderedUnions.SelectMany(static union => new[] { union.Name, union.UnknownTypeName, }))
            .Distinct(_comparer)
            .Order(_comparer)
            .ToArray();
        return new SchemaBindingResult
        {
            Models = orderedModels,
            Unions = orderedUnions,
            Registry = new RegistryPlan
            {
                TypeNames = registryNames,
            },
        };
    }

    private static List<UnionPlan> BindExplicitUnions(SpecDocument document, ReachableSchemaSet reachable, HashSet<string> responseRoots,
        HashSet<string> streamCauseKeys, IReadOnlyDictionary<string, string> names, Dictionary<string, List<string>> inheritance,
        SchemaInhabitationPolicy inhabitation, BindingErrorCollector errors)
    {
        var plans = new Dictionary<string, UnionPlan>(StringComparer.Ordinal);
        var fixedMarkers = new Dictionary<string, UnionFixedMarkerPlan>(StringComparer.Ordinal);
        foreach (var key in reachable.GraphKeys)
        {
            if (responseRoots.Contains(key)
                || !document.Schemas.TryGetValue(key, out var schema)
                || schema is not UnionNode { Classification: UnionClassification.Marked } union
                || !names.TryGetValue(key, out var name))
            {
                continue;
            }

            var plan = BindUnion(name, key, union, document.Schemas, names, inheritance, fixedMarkers, inhabitation, errors);
            if (plan is not null)
            {
                plans.Add(key, plan);
            }
        }

        // A nested union learns its base type and fixed outer marker while the outer union
        // binds, which can happen after the nested plan was built — the wiring is a post-pass.
        var result = new List<UnionPlan>(plans.Count);
        foreach (var (key, plan) in plans)
        {
            var wired = plan;
            if (inheritance.TryGetValue(key, out var baseTypes) && baseTypes.Count is 1)
            {
                wired = wired with
                {
                    BaseTypeName = baseTypes[0]
                };
            }

            if (fixedMarkers.TryGetValue(key, out var fixedMarker))
            {
                wired = wired with
                {
                    FixedMarker = fixedMarker
                };
            }

            if (streamCauseKeys.Contains(key))
            {
                wired = wired with
                {
                    BaseTypeName = EffectStreamTypeNamePolicy.CauseMarkerInterface
                };
            }

            result.Add(wired);
        }

        return result;
    }

    private static UnionPlan? BindUnion(string name, string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph,
        IReadOnlyDictionary<string, string> names, Dictionary<string, List<string>> inheritance,
        Dictionary<string, UnionFixedMarkerPlan> fixedMarkers, SchemaInhabitationPolicy inhabitation, BindingErrorCollector errors)
    {
        var resolved = ResolveBranches(key, union, graph, names, inhabitation, errors);
        if (resolved is null)
        {
            return null;
        }

        var marker = SelectDiscriminatingMarker(resolved);
        if (marker is null)
        {
            errors.Add(BindingErrorCategory.Schema, key, "marked union branches share no discriminating marker property");
            return null;
        }

        var variants = new List<UnionVariantPlan>(resolved.Count);
        var knownImpossibleTags = new List<string>();
        foreach (var branch in resolved)
        {
            var tag = branch.Markers.First(candidate =>
                    string.Equals(candidate.PropertyName, marker.PropertyName, StringComparison.Ordinal))
                .Value;
            if (!branch.IsInhabited)
            {
                knownImpossibleTags.Add(tag);
                continue;
            }

            AddInheritance(branch.MemberKey, name, inheritance, errors);
            if (branch.IsNestedUnion)
            {
                fixedMarkers[branch.MemberKey] = new UnionFixedMarkerPlan
                {
                    WireName = marker.PropertyName,
                    Name = CSharpNamePolicy.ToPascalCase(marker.PropertyName),
                    Kind = marker.Kind,
                    Value = tag,
                };
            }

            variants.Add(new UnionVariantPlan
            {
                TypeName = branch.TypeName,
                Tag = tag,
                IsNestedUnion = branch.IsNestedUnion,
            });
        }

        var conceptName = CSharpNamePolicy.ToUnionConceptName(name);
        return new UnionPlan
        {
            Name = name,
            ConceptName = conceptName,
            Namespace = GeneratedNamespace.Models,
            UnknownTypeName = $"Unknown{conceptName}",
            MarkerWireName = marker.PropertyName,
            MarkerName = CSharpNamePolicy.ToPascalCase(marker.PropertyName),
            MarkerKind = marker.Kind,
            Variants = variants,
            KnownImpossibleTags = [.. knownImpossibleTags.Order(StringComparer.Ordinal)],
            Description = union.Description,
        };
    }

    private static List<ResolvedUnionBranch>? ResolveBranches(string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph,
        IReadOnlyDictionary<string, string> names, SchemaInhabitationPolicy inhabitation, BindingErrorCollector errors)
    {
        var resolved = new List<ResolvedUnionBranch>(union.Branches.Count);
        foreach (var branch in union.Branches)
        {
            if (branch is not RefNode reference
                || !graph.TryGetValue(reference.Target, out var target)
                || !names.TryGetValue(reference.Target, out var typeName))
            {
                errors.Add(
                    BindingErrorCategory.Schema,
                    key,
                    "marked union branch must reference a named object or nested marked union with a literal marker");
                continue;
            }

            // A nested union that fixes the outer marker behaves like one tag. One that
            // discriminates on the outer marker instead spans it, so the outer dispatches
            // straight to that union's own leaves rather than through a second converter.
            if (target is UnionNode { Classification: UnionClassification.Marked } nested
                && ResolveUniformNestedMarkers(nested, graph) is not { Count: > 0 })
            {
                var spanned = ResolveSpannedBranches(nested, reference.Target, graph, names, inhabitation);
                if (spanned is null)
                {
                    errors.Add(
                        BindingErrorCategory.Schema,
                        key,
                        "marked union branch must reference a named object or nested marked union with a literal marker");
                    continue;
                }

                resolved.AddRange(spanned);
                continue;
            }

            var markers = target switch
            {
                ObjectNode { LiteralMarkers.Count: > 0 } objectNode => objectNode.LiteralMarkers,
                UnionNode { Classification: UnionClassification.Marked } uniform => ResolveUniformNestedMarkers(uniform, graph),
                _ => null,
            };
            if (markers is not { Count: > 0 })
            {
                errors.Add(
                    BindingErrorCategory.Schema,
                    key,
                    "marked union branch must reference a named object or nested marked union with a literal marker");
                continue;
            }

            resolved.Add(new ResolvedUnionBranch(
                typeName,
                markers,
                target is UnionNode,
                reference.Target,
                inhabitation.IsInhabited(target)));
        }

        // A spanning branch contributes several entries under one member key, so the arity
        // check counts branches answered rather than entries produced.
        var answered = resolved.Select(static branch => branch.MemberKey).Distinct(StringComparer.Ordinal).Count();
        return answered == union.Branches.Count ? resolved : null;
    }

    /// <summary>Expands a marker-spanning nested union into the leaves the outer dispatches to.</summary>
    private static List<ResolvedUnionBranch>? ResolveSpannedBranches(UnionNode nested, string nestedKey,
        IReadOnlyDictionary<string, SchemaNode> graph, IReadOnlyDictionary<string, string> names,
        SchemaInhabitationPolicy inhabitation)
    {
        var result = new List<ResolvedUnionBranch>(nested.Branches.Count);
        foreach (var branch in nested.Branches)
        {
            // Only one level is admitted: a leaf of a spanning union must itself be a marked
            // object, never another union.
            if (branch is not RefNode reference
                || !graph.TryGetValue(reference.Target, out var target)
                || target is not ObjectNode { LiteralMarkers.Count: > 0 } leaf
                || !names.TryGetValue(reference.Target, out var typeName))
            {
                return null;
            }

            result.Add(new ResolvedUnionBranch(
                typeName,
                leaf.LiteralMarkers,
                IsNestedUnion: false,
                nestedKey,
                inhabitation.IsInhabited(leaf)));
        }

        return result;
    }

    /// <summary>
    /// Resolves the markers every variant of a nested union fixes to one shared value — for
    /// those properties the whole nested union behaves like a single tag.
    /// </summary>
    private static IReadOnlyList<LiteralMarker>? ResolveUniformNestedMarkers(UnionNode nested,
        IReadOnlyDictionary<string, SchemaNode> graph)
    {
        var variantMarkers = new List<IReadOnlyList<LiteralMarker>>(nested.Branches.Count);
        foreach (var branch in nested.Branches)
        {
            if (branch is not RefNode reference
                || !graph.TryGetValue(reference.Target, out var target)
                || target is not ObjectNode { LiteralMarkers.Count: > 0 } objectNode)
            {
                return null;
            }

            variantMarkers.Add(objectNode.LiteralMarkers);
        }

        return
        [
            .. variantMarkers[0]
                .Where(candidate => variantMarkers
                    .Skip(1)
                    .All(markers => markers.Any(other =>
                        string.Equals(other.PropertyName, candidate.PropertyName, StringComparison.Ordinal)
                        && other.Kind == candidate.Kind
                        && string.Equals(other.Value, candidate.Value, StringComparison.Ordinal)))),
        ];
    }

    /// <summary>
    /// Selects the first property (in the first branch's document order) that every branch
    /// carries with one kind and a value distinct from every other branch.
    /// </summary>
    private static LiteralMarker? SelectDiscriminatingMarker(IReadOnlyList<ResolvedUnionBranch> branches)
    {
        foreach (var candidate in branches[0].Markers)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            var qualified = true;
            foreach (var branch in branches)
            {
                var match = branch.Markers.FirstOrDefault(marker =>
                    string.Equals(marker.PropertyName, candidate.PropertyName, StringComparison.Ordinal)
                    && marker.Kind == candidate.Kind);
                if (match is null || !values.Add(match.Value))
                {
                    qualified = false;
                    break;
                }
            }

            if (qualified)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// One dispatch entry of a union. <paramref name="MemberKey"/> is the schema that records
    /// membership; when a nested union spans the outer marker, that member differs from the
    /// leaf represented by <paramref name="TypeName"/>.
    /// </summary>
    private sealed record ResolvedUnionBranch(
        string TypeName,
        IReadOnlyList<LiteralMarker> Markers,
        bool IsNestedUnion,
        string MemberKey,
        bool IsInhabited);

    private static UnionPlan? BindErrorUnion(SpecDocument document, ReachableSchemaSet reachable, HashSet<string> responseRoots,
        HashSet<string> streamCauseKeys, IReadOnlyDictionary<string, string> names,
        IDictionary<string, List<string>> inheritance, BindingErrorCollector errors)
    {
        var errorsInClosure = new List<KeyValuePair<string, ObjectNode>>();
        foreach (var key in reachable.GraphKeys)
        {
            if (!responseRoots.Contains(key)
                && !streamCauseKeys.Contains(key)
                && document.Schemas.TryGetValue(key, out var schema)
                && schema is ObjectNode { ErrorStyle: not ErrorStyle.None } objectNode)
            {
                errorsInClosure.Add(new KeyValuePair<string, ObjectNode>(key, objectNode));
            }
        }

        if (errorsInClosure.Count is 0)
        {
            return null;
        }

        var styles = errorsInClosure.Select(static pair => pair.Value.ErrorStyle).Distinct().ToArray();
        if (styles is not [ErrorStyle.EffectTag])
        {
            errors.Add(BindingErrorCategory.Schema, "OpenCodeError", "selected errors must use the Effect _tag style in M1");
            return null;
        }

        var variants = new List<UnionVariantPlan>(errorsInClosure.Count);
        foreach (var (key, objectNode) in errorsInClosure.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!names.TryGetValue(key, out var typeName))
            {
                errors.Add(BindingErrorCategory.Naming, key, "error schema has no unique C# type name");
                continue;
            }

            var markers = objectNode.LiteralMarkers.Where(static marker => marker.PropertyName is "_tag").ToArray();
            if (markers is not [var marker])
            {
                errors.Add(BindingErrorCategory.Schema, key, "Effect error must declare exactly one required _tag literal");
                continue;
            }

            AddInheritance(key, CSharpNamePolicy.ToUnionInterfaceName("OpenCodeError"), inheritance, errors);
            variants.Add(new UnionVariantPlan
            {
                TypeName = typeName,
                Tag = marker.Value,
            });
        }

        // A tag owned by two closure types would poison the converter's dispatch map at its
        // first use; structurally identical duplicates have the schema-alias escape.
        var duplicateTags = variants
            .GroupBy(static variant => variant.Tag, StringComparer.Ordinal)
            .Where(static group => group.Skip(1).Any())
            .Select(static group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicateTags.Length > 0)
        {
            foreach (var tag in duplicateTags)
            {
                errors.Add(BindingErrorCategory.Schema, "OpenCodeError", $"multiple error schemas declare tag '{tag}'");
            }

            return null;
        }

        return new UnionPlan
        {
            Name = CSharpNamePolicy.ToUnionInterfaceName("OpenCodeError"),
            ConceptName = "OpenCodeError",
            Namespace = GeneratedNamespace.Models,
            UnknownTypeName = "UnknownOpenCodeError",
            MarkerWireName = "_tag",
            MarkerName = "Tag",
            MarkerKind = LiteralKind.String,
            Variants = [.. variants.OrderBy(static variant => variant.TypeName, StringComparer.Ordinal)],
            Description = "Represents a typed error returned by the opencode API.",
        };
    }

    private static ObjectModelPlan? BindObject(string key, string name, ObjectNode node, Dictionary<string, List<string>> inheritance,
        TypePlanBinder typeBinder, BindingErrorCollector errors)
    {
        if (node.AdditionalProperties is AdditionalPropertiesKind.Schema)
        {
            errors.Add(
                BindingErrorCategory.Schema,
                string.Concat(key, "/additionalProperties"),
                "named properties and schema-valued additional properties cannot be represented without data loss");
            return null;
        }

        var properties = new List<ModelPropertyPlan>(node.Properties.Count);
        foreach (var property in node.Properties)
        {
            var type = typeBinder.Bind(key, property.Name, property.Schema);
            if (type is null)
            {
                continue;
            }

            if (!property.IsRequired)
            {
                type = type with
                {
                    IsNullable = true
                };
            }

            var literal = property.Schema as LiteralNode;
            properties.Add(new ModelPropertyPlan
            {
                WireName = property.Name,
                Name = CSharpNamePolicy.ToPascalCase(property.Name),
                Type = type,
                IsRequired = property.IsRequired,
                IsLiteral = literal is not null,
                LiteralKind = literal?.Kind,
                LiteralValue = literal?.Value,
                Description = property.Schema.Description,
            });
        }

        if (properties.Count != node.Properties.Count)
        {
            return null;
        }

        var duplicate = properties
            .GroupBy(static property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Skip(1).Any());

        if (duplicate is not null)
        {
            errors.Add(BindingErrorCategory.Naming, key, $"multiple properties map to C# name '{duplicate.Key}'");
            return null;
        }

        return new ObjectModelPlan
        {
            Name = name,
            Namespace = GeneratedNamespace.Models,
            Description = node.Description,
            Properties = properties,
            ImplementedUnionNames = inheritance.TryGetValue(key, out var implemented) ? implemented : [],
        };
    }

    private static EnumModelPlan BindEnum(string name, EnumNode node, BindingErrorCollector errors)
    {
        var values = node
            .Values.Select(value => new EnumValuePlan
            {
                Name = CSharpNamePolicy.ToPascalCase(value),
                WireValue = value,
            })
            .ToArray();
        var duplicate = values.GroupBy(static value => value.Name, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            errors.Add(BindingErrorCategory.Naming, name, $"multiple enum values map to C# name '{duplicate.Key}'");
        }

        return new EnumModelPlan
        {
            Name = name,
            Namespace = GeneratedNamespace.Models,
            Description = node.Description,
            Values = values,
        };
    }

    /// <summary>
    /// Records one union a schema is a branch of. A schema can be a branch of several, so
    /// membership accumulates rather than refusing the second (ADR-0011).
    /// </summary>
    private static void AddInheritance(string schemaKey, string unionName, IDictionary<string, List<string>> inheritance,
        BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (!inheritance.TryGetValue(schemaKey, out var unions))
        {
            unions = [];
            inheritance[schemaKey] = unions;
        }

        if (!unions.Contains(unionName, StringComparer.Ordinal))
        {
            unions.Add(unionName);
        }
    }
}
