using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenCode.Sdk.Tools.Generator.Emission;
using OpenCode.Sdk.Tools.Tests.Support;

namespace OpenCode.Sdk.Tools.Tests.Generator.Emission;

public sealed class SourceEmitterTests
{
    [Test]
    public async Task Emit_Should_Return_Ordinal_Sorted_Byte_Identical_Source()
    {
        var plan = EmitterPlanFixture.Create();

        var first = SourceEmitter.Emit(plan);
        var second = SourceEmitter.Emit(plan);

        await Assert.That(first.Select(static source => source.RelativePath)
            .SequenceEqual(first.Select(static source => source.RelativePath).Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        await Assert.That(first.Select(static source => source.Utf8Source.ToArray())
            .Zip(second.Select(static source => source.Utf8Source.ToArray()), static (left, right) => left.SequenceEqual(right))
            .All(static equal => equal)).IsTrue();
    }

    [Test]
    public async Task Emit_Should_Preserve_All_Bound_Union_And_Registry_Evidence_Through_Source_Generation_And_Compilation()
    {
        var plan = await new BindingTestHost().BindPinnedAsync();
        var sources = SourceEmitter.Emit(plan);

        foreach (var union in plan.Unions)
        {
            var source = sources.Single(candidate => candidate.RelativePath ==
                                                     $"Internal/Serialization/{union.ConceptName}JsonConverter.cs");
            var expected = union
                .Variants
                .OrderBy(static variant => variant.Tag, StringComparer.Ordinal)
                .Select(static variant => new KeyValuePair<string, string>(variant.Tag, variant.TypeName));
            await Assert.That(ReadConverterMappings(source).SequenceEqual(expected)).IsTrue();
        }

        var requiredRegistryTypes = plan
            .Unions
            .SelectMany(static union => union
                .Variants.Select(static variant => variant.TypeName)
                .Append(union.Name)
                .Append(union.UnknownTypeName))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(requiredRegistryTypes.Except(plan.Registry.TypeNames, StringComparer.Ordinal)).IsEmpty();

        var registrySource = sources.Single(static source =>
            source.RelativePath == "Internal/Serialization/OpenCodeJsonContext.cs");
        var emittedRegistryTypes = ReadRegistryTypes(registrySource);
        await Assert
            .That(emittedRegistryTypes.SequenceEqual(
                plan.Registry.TypeNames.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
            .IsTrue();
        await Assert.That(requiredRegistryTypes.Except(emittedRegistryTypes, StringComparer.Ordinal)).IsEmpty();

        var diagnostics = await GeneratedSourceCompiler.CompileWithSdkCoreAsync(sources);

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task Emit_Should_Not_Emit_Retired_Materialization_Helpers()
    {
        var sources = SourceEmitter.Emit(EmitterPlanFixture.Create());

        await Assert.That(sources.Select(static source => source.RelativePath)).DoesNotContain(
            "Internal/Serialization/OptionalCollectionInput.cs");
        var content = EmitterSnapshot.Create(sources);
        await Assert.That(content).DoesNotContain("WireNullRejecting");
        await Assert.That(content).DoesNotContain("NullElementRejectingListJsonConverter");
        await Assert.That(content).DoesNotContain("ListPayloadInput");
        await Assert.That(content).DoesNotContain("NonEmptyNoContentFailure");
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ReadConverterMappings(GeneratedSource source)
    {
        var root = Parse(source);
        return
        [
            .. root
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(static assignment =>
                    assignment.Left is ImplicitElementAccessSyntax && assignment.Right is TypeOfExpressionSyntax)
                .Select(static assignment => new KeyValuePair<string, string>(
                    ((LiteralExpressionSyntax)((ImplicitElementAccessSyntax)assignment.Left)
                        .ArgumentList.Arguments.Single()
                        .Expression).Token.ValueText,
                    ((TypeOfExpressionSyntax)assignment.Right).Type.ToString()))
                .OrderBy(static mapping => mapping.Key, StringComparer.Ordinal),
        ];
    }

    private static IReadOnlyList<string> ReadRegistryTypes(GeneratedSource source)
    {
        var root = Parse(source);
        return
        [
            .. root
                .DescendantNodes()
                .OfType<AttributeSyntax>()
                .Where(static attribute => attribute.Name.ToString() == "JsonSerializable")
                .Select(static attribute =>
                    ((TypeOfExpressionSyntax)attribute.ArgumentList!.Arguments.Single().Expression).Type.ToString())
                .Order(StringComparer.Ordinal),
        ];
    }

    private static CompilationUnitSyntax Parse(GeneratedSource source) =>
        CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(source.Utf8Source.Span)).GetCompilationUnitRoot();
}
