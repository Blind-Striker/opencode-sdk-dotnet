using OpenCode.Sdk.Tools.Generator.Binding.Models;
using OpenCode.Sdk.Tools.Generator.Ingestion.Models;

namespace OpenCode.Sdk.Tools.Generator.Binding;

internal sealed class SchemaPlanBinder(SchemaNameResolver schemaNames)
{
    private const string ModelNamespace = "OpenCode.Sdk.Models";
    private readonly SchemaNameResolver _schemaNames = schemaNames ?? throw new ArgumentNullException(nameof(schemaNames));

    public SchemaBindingResult Bind(SpecDocument document, ReachableSchemaSet reachable, GenerationCuration curation,
        BindingErrorCollector errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reachable);
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(errors);

        var typeNames = _schemaNames.Resolve(document, reachable, errors);
        var responseRoots = reachable.ResponseRootKeys.ToHashSet(StringComparer.Ordinal);
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

        var orderedModels = models.OrderBy(static model => model.Name, StringComparer.Ordinal).ToArray();
        var orderedUnions = unions.OrderBy(static union => union.Name, StringComparer.Ordinal).ToArray();
        var registryNames = orderedModels.Select(static model => model.Name)
            .Concat(orderedUnions.SelectMany(static union => new[] { union.Name, union.UnknownTypeName, }))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
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
        IReadOnlyDictionary<string, string> names, IDictionary<string, string> inheritance, BindingErrorCollector errors)
    {
        var result = new List<UnionPlan>();
        foreach (var key in reachable.GraphKeys)
        {
            if (responseRoots.Contains(key)
                || !document.Schemas.TryGetValue(key, out var schema)
                || schema is not UnionNode { Classification: UnionClassification.Marked } union
                || !names.TryGetValue(key, out var name))
            {
                continue;
            }

            var plan = BindUnion(name, key, union, document.Schemas, names, inheritance, errors);
            if (plan is not null)
            {
                result.Add(plan);
            }
        }

        return result;
    }

    private static UnionPlan? BindUnion(string name, string key, UnionNode union, IReadOnlyDictionary<string, SchemaNode> graph,
        IReadOnlyDictionary<string, string> names, IDictionary<string, string> inheritance, BindingErrorCollector errors)
    {
        var variants = new List<UnionVariantPlan>(union.Branches.Count);
        LiteralMarker? expectedMarker = null;
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in union.Branches)
        {
            if (branch is not RefNode reference
                || !graph.TryGetValue(reference.Target, out var target)
                || target is not ObjectNode { LiteralMarkers.Count: > 0 } objectNode
                || !names.TryGetValue(reference.Target, out var typeName))
            {
                errors.Add(BindingErrorCategory.Schema, key, "marked union branch must reference a named object with a literal marker");
                continue;
            }

            var marker = objectNode.LiteralMarkers[0];
            expectedMarker ??= marker;
            if (!string.Equals(marker.PropertyName, expectedMarker.PropertyName, StringComparison.Ordinal) || marker.Kind != expectedMarker.Kind)
            {
                errors.Add(BindingErrorCategory.Schema, key, "marked union branches do not share one marker property and kind");
                continue;
            }

            if (!tags.Add(marker.Value))
            {
                errors.Add(BindingErrorCategory.Schema, key, $"marked union tag '{marker.Value}' is duplicated");
                continue;
            }

            AddInheritance(reference.Target, name, inheritance, errors);
            variants.Add(new UnionVariantPlan
            {
                TypeName = typeName,
                Tag = marker.Value,
            });
        }

        return expectedMarker is null || variants.Count != union.Branches.Count
            ? null
            : new UnionPlan
            {
                Name = name,
                Namespace = ModelNamespace,
                UnknownTypeName = $"Unknown{name}",
                MarkerWireName = expectedMarker.PropertyName,
                MarkerName = CSharpNamePolicy.ToPascalCase(expectedMarker.PropertyName),
                MarkerKind = expectedMarker.Kind,
                Variants = variants,
                Description = union.Description,
            };
    }

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

        var duplicate = properties.GroupBy(static property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
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
            LiteralNode literal => Named(literal.Kind is LiteralKind.Boolean ? "bool" : "string"),
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
