using System.IO.Abstractions;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed record ScenarioContext(IFileSystem FileSystem, string SpecPath);
