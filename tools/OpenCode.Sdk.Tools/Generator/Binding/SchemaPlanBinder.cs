using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SchemaPlanBinder
{
    private const string ModelNamespace = "OpenCode.Sdk.Models";
    private readonly StringComparer _comparer = StringComparer.Ordinal;

    public SchemaBindingResult Bind(SpecDocument document, ReachableSchemaSet reachable, GenerationCuration curation,
        IReadOnlyDictionary<string, string> typeNames, BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(errors);

        var responseRoots = reachable.ResponseRootKeys.ToHashSet(_comparer);
        RefuseStructuralUnions(document, reachable, errors);

        var inheritance = new Dictionary<string, string>(StringComparer.Ordinal);
        var unions = BindExplicitUnions(document, reachable, responseRoots, typeNames, inheritance, errors);
        var errorUnion = BindErrorUnion(document, reachable, responseRoots, typeNames, inheritance, errors);
        if (errorUnion is not null)
        {
            unions.Add(errorUnion);
        }

        var typeBinder = new TypePlanBinder(document.Schemas, typeNames, curation.PropertyOverrides, errors);
        var models = new List<ModelPlan>();
        foreach (var key in reachable.GraphKeys)
        {
            if (responseRoots.Contains(key) || !document.Schemas.TryGetValue(key, out var schema) || !typeNames.TryGetValue(key, out var name))
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
            }
        }

        var orderedModels = models.OrderBy(static model => model.Name, _comparer).ToArray();
        var orderedUnions = unions.OrderBy(static union => union.Name, _comparer).ToArray();
        var registryNames = orderedModels.Select(static model => model.Name)
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

    private static void RefuseStructuralUnions(SpecDocument document, ReachableSchemaSet reachable, BindingErrorCollector errors)
    {
        foreach (var key in reachable.GraphKeys)
        {
            if (document.Schemas.TryGetValue(key, out var schema) && schema is UnionNode { Classification: UnionClassification.Structural })
            {
                errors.Add(BindingErrorCategory.Schema, key, "selected closure contains a structural union");
            }
        }
    }

    private static List<UnionPlan> BindExplicitUnions(SpecDocument document, ReachableSchemaSet reachable, HashSet<string> responseRoots,
        IReadOnlyDictionary<string, string> names, Dictionary<string, string> inheritance, BindingErrorCollector errors)
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

            var plan = BindUnion(name, key, union, document.Schemas, names, inheritance, fixedMarkers, errors);
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
            result.Add(fixedMarkers.TryGetValue(key, out var fixedMarker) && inheritance.TryGetValue(key, out var baseType)
                ? plan with { BaseTypeName = baseType, FixedMarker = fixedMarker }
                : plan);
        }

        return result;
    }

    private static UnionPlan? BindUnion(string name, string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph,
        IReadOnlyDictionary<string, string> names, Dictionary<string, string> inheritance,
        Dictionary<string, UnionFixedMarkerPlan> fixedMarkers, BindingErrorCollector errors)
    {
        var resolved = ResolveBranches(key, union, graph, names, errors);
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
        foreach (var branch in resolved)
        {
            var tag = branch.Markers.First(candidate =>
                string.Equals(candidate.PropertyName, marker.PropertyName, StringComparison.Ordinal)).Value;
            AddInheritance(branch.TargetKey, name, inheritance, errors);
            if (branch.IsNestedUnion)
            {
                fixedMarkers[branch.TargetKey] = new UnionFixedMarkerPlan
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

        return new UnionPlan
        {
            Name = name,
            Namespace = ModelNamespace,
            UnknownTypeName = $"Unknown{name}",
            MarkerWireName = marker.PropertyName,
            MarkerName = CSharpNamePolicy.ToPascalCase(marker.PropertyName),
            MarkerKind = marker.Kind,
            Variants = variants,
            Description = union.Description,
        };
    }

    private static List<ResolvedUnionBranch>? ResolveBranches(string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph,
        IReadOnlyDictionary<string, string> names, BindingErrorCollector errors)
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

            var markers = target switch
            {
                ObjectNode { LiteralMarkers.Count: > 0 } objectNode => objectNode.LiteralMarkers,
                UnionNode { Classification: UnionClassification.Marked } nested => ResolveUniformNestedMarkers(nested, graph),
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

            resolved.Add(new ResolvedUnionBranch(reference.Target, typeName, markers, target is UnionNode));
        }

        return resolved.Count == union.Branches.Count ? resolved : null;
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
                .Where(candidate => variantMarkers.Skip(1).All(markers => markers.Any(other =>
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

    private sealed record ResolvedUnionBranch(string TargetKey, string TypeName, IReadOnlyList<LiteralMarker> Markers, bool IsNestedUnion);

    private static UnionPlan? BindErrorUnion(SpecDocument document, ReachableSchemaSet reachable, HashSet<string> responseRoots,
        IReadOnlyDictionary<string, string> names, IDictionary<string, string> inheritance, BindingErrorCollector errors)
    {
        var errorsInClosure = new List<KeyValuePair<string, ObjectNode>>();
        foreach (var key in reachable.GraphKeys)
        {
            if (!responseRoots.Contains(key)
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

            AddInheritance(key, "OpenCodeError", inheritance, errors);
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
            Name = "OpenCodeError",
            Namespace = ModelNamespace,
            UnknownTypeName = "UnknownOpenCodeError",
            MarkerWireName = "_tag",
            MarkerName = "Tag",
            MarkerKind = LiteralKind.String,
            Variants = [.. variants.OrderBy(static variant => variant.TypeName, StringComparer.Ordinal)],
            Description = "Represents a typed error returned by the opencode API.",
        };
    }

    private static ObjectModelPlan? BindObject(string key, string name, ObjectNode node, IReadOnlyDictionary<string, string> inheritance,
        TypePlanBinder typeBinder, BindingErrorCollector errors)
    {
        var properties = new List<ModelPropertyPlan>(node.Properties.Count);
        foreach (var property in node.Properties)
        {
            var type = typeBinder.Bind(key, property.Name, property.Schema);
            if (type is null)
            {
                continue;
            }

            // Captured before optional-absence widening: schema-admitted null is what the type
            // binder reported, plus JsonElement, whose unrestricted schema admits every JSON
            // value including null.
            var allowsWireNull = type.IsNullable || type is NamedTypeReferencePlan { Name: "JsonElement" };
            if (!property.IsRequired && !type.IsCollection)
            {
                type = type with { IsNullable = true };
            }

            var literal = property.Schema as LiteralNode;
            properties.Add(new ModelPropertyPlan
            {
                WireName = property.Name,
                Name = CSharpNamePolicy.ToPascalCase(property.Name),
                Type = type,
                IsRequired = property.IsRequired,
                AllowsWireNull = allowsWireNull,
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
            Namespace = ModelNamespace,
            Description = node.Description,
            Properties = properties,
            BaseTypeName = inheritance.GetValueOrDefault(key),
        };
    }

    private static EnumModelPlan BindEnum(string name, EnumNode node, BindingErrorCollector errors)
    {
        var values = node.Values.Select(value => new EnumValuePlan
        {
            Name = CSharpNamePolicy.ToPascalCase(value),
            WireValue = value,
        }).ToArray();
        var duplicate = values.GroupBy(static value => value.Name, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            errors.Add(BindingErrorCategory.Naming, name, $"multiple enum values map to C# name '{duplicate.Key}'");
        }

        return new EnumModelPlan
        {
            Name = name,
            Namespace = ModelNamespace,
            Description = node.Description,
            Values = values,
        };
    }

    private static void AddInheritance(string schemaKey, string baseType, IDictionary<string, string> inheritance,
        BindingErrorCollector errors)
    {
        if (inheritance.TryGetValue(schemaKey, out var existing) && !string.Equals(existing, baseType, StringComparison.Ordinal))
        {
            errors.Add(BindingErrorCategory.Schema, schemaKey, $"schema cannot derive from both '{existing}' and '{baseType}'");
            return;
        }

        inheritance[schemaKey] = baseType;
    }

    private sealed class TypePlanBinder
    {
        private readonly IReadOnlyDictionary<string, SchemaNode> _graph;
        private readonly IReadOnlyDictionary<string, string> _names;
        private readonly Dictionary<string, PropertyOverrideType> _overrides;
        private readonly BindingErrorCollector _errors;

        public TypePlanBinder(IReadOnlyDictionary<string, SchemaNode> graph, IReadOnlyDictionary<string, string> names,
            IReadOnlyList<PropertyOverride> overrides, BindingErrorCollector errors)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _names = names ?? throw new ArgumentNullException(nameof(names));
            ArgumentNullException.ThrowIfNull(overrides);
            _overrides = new Dictionary<string, PropertyOverrideType>(StringComparer.Ordinal);
            foreach (var propertyOverride in overrides)
            {
                _overrides.TryAdd($"{propertyOverride.Schema}\0{propertyOverride.Property}", propertyOverride.Type);
            }

            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public TypeReferencePlan? Bind(string schemaKey, string propertyName, SchemaNode schema)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schemaKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
            ArgumentNullException.ThrowIfNull(schema);

            var type = BindCore(schema, $"{schemaKey}.{propertyName}", []);
            if (type is null || !_overrides.TryGetValue($"{schemaKey}\0{propertyName}", out var propertyOverride))
            {
                return type;
            }

            if (propertyOverride is PropertyOverrideType.Uri && type is NamedTypeReferencePlan { Name: "string" })
            {
                return new NamedTypeReferencePlan
                {
                    Name = "Uri",
                    IsNullable = type.IsNullable,
                };
            }

            _errors.Add(BindingErrorCategory.Curation, $"{schemaKey}.{propertyName}", "uri override requires a scalar string property");
            return null;
        }

        private TypeReferencePlan? BindCore(SchemaNode schema, string subject, HashSet<string> aliases) => schema switch
        {
            RefNode reference => BindReference(reference, subject, aliases),
            PrimitiveNode primitive => BindPrimitive(primitive),
            LiteralNode { Kind: LiteralKind.Boolean } => Named("bool"),
            LiteralNode { Kind: LiteralKind.String } => Named("string"),
            LiteralNode => Refuse(subject, "numeric literal schemas are not supported by the emitter"),
            ArrayNode array => BindArray(array, subject, aliases),
            DictionaryNode dictionary => BindDictionary(dictionary, subject, aliases),
            FreeFormObjectNode => DictionaryOf(Named("JsonElement")),
            UnrestrictedNode => Named("JsonElement"),
            SpecialNumberNode => Named("double"),
            NullableNode nullable => BindNullable(nullable, subject, aliases),
            JsonStringNode => Refuse(subject, "JSON-encoded strings are not supported by the M1 emitter"),
            TupleNode => Refuse(subject, "tuple schemas are not supported by the M1 emitter"),
            ObjectNode or EnumNode or UnionNode => Refuse(subject, "inline nominal schema was not promoted into the graph"),
            _ => Refuse(subject, $"schema node '{schema.GetType().Name}' is not supported by the M1 emitter"),
        };

        private TypeReferencePlan? BindReference(RefNode reference, string subject, HashSet<string> aliases)
        {
            if (_names.TryGetValue(reference.Target, out var name))
            {
                return Named(name);
            }

            if (!_graph.TryGetValue(reference.Target, out var target))
            {
                return Refuse(subject, $"schema reference '{reference.Target}' is missing");
            }

            if (!aliases.Add(reference.Target))
            {
                return Refuse(subject, $"non-nominal schema alias cycle reaches '{reference.Target}'");
            }

            var result = BindCore(target, subject, aliases);
            _ = aliases.Remove(reference.Target);
            return result;
        }

        private static NamedTypeReferencePlan BindPrimitive(PrimitiveNode primitive) => primitive.Kind switch
        {
            PrimitiveKind.String when string.Equals(primitive.Format, "uri", StringComparison.Ordinal) => Named("Uri"),
            PrimitiveKind.String => Named("string"),
            PrimitiveKind.Number => Named("double"),
            PrimitiveKind.Integer => Named("long"),
            PrimitiveKind.Boolean => Named("bool"),
            _ => throw new InvalidOperationException($"Unknown primitive kind '{primitive.Kind}'."),
        };

        private ListTypeReferencePlan? BindArray(ArrayNode array, string subject, HashSet<string> aliases)
        {
            var item = BindCore(array.Item, subject, aliases);
            return item is null ? null : ListOf(item);
        }

        private DictionaryTypeReferencePlan? BindDictionary(DictionaryNode dictionary, string subject, HashSet<string> aliases)
        {
            var value = BindCore(dictionary.Value, subject, aliases);
            return value is null ? null : DictionaryOf(value);
        }

        private TypeReferencePlan? BindNullable(NullableNode nullable, string subject, HashSet<string> aliases)
        {
            var inner = BindCore(nullable.Inner, subject, aliases);
            return inner is null ? null : inner with { IsNullable = true };
        }

        private TypeReferencePlan? Refuse(string subject, string problem)
        {
            _errors.Add(BindingErrorCategory.Schema, subject, problem);
            return null;
        }

        private static NamedTypeReferencePlan Named(string name) =>
            new()
            {
                Name = name,
                IsNullable = false,
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
}
