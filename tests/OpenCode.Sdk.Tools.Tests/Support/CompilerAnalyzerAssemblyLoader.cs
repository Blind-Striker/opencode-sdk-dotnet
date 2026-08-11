using Microsoft.CodeAnalysis;

namespace OpenCode.Sdk.Tools.Tests.Support;

internal sealed class CompilerAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    public void AddDependencyLocation(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
    }

    public System.Reflection.Assembly LoadFromPath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
