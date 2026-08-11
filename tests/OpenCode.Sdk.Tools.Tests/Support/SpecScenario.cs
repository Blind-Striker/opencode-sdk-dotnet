using Testably.Abstractions.Testing;
using Testably.Abstractions.Testing.Initializer;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal abstract class SpecScenario
{
    private const string CanonicalSpecPath = "spec/openapi.json";

    public static SpecScenario Define(Action<SpecDocumentBuilder> arrange)
    {
        ArgumentNullException.ThrowIfNull(arrange);
        return new DefinedSpecScenario(arrange);
    }

    public static SpecScenario FromRawJson(string rawJson)
    {
        ArgumentNullException.ThrowIfNull(rawJson);
        return new RawJsonSpecScenario(rawJson);
    }

    public ScenarioContext Build()
    {
        var fileSystem = new MockFileSystem();
        var payload = CreatePayload(new FixtureLoader());
        fileSystem.Initialize().With(new FileDescription(CanonicalSpecPath, payload));

        return new ScenarioContext(fileSystem, CanonicalSpecPath);
    }

    protected virtual void Arrange(SpecDocumentBuilder spec)
    {
    }

    private protected virtual string CreatePayload(FixtureLoader fixtureLoader)
    {
        var document = new SpecDocumentBuilder(fixtureLoader);
        Arrange(document);
        return document.BuildJson();
    }

    private sealed class DefinedSpecScenario(Action<SpecDocumentBuilder> arrange) : SpecScenario
    {
        protected override void Arrange(SpecDocumentBuilder spec) => arrange(spec);
    }

    private sealed class RawJsonSpecScenario(string rawJson) : SpecScenario
    {
        private protected override string CreatePayload(FixtureLoader fixtureLoader) => rawJson;
    }
}
