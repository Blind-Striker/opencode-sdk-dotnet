using BenchmarkDotNet.Running;

namespace OpenCode.Sdk.Performance.Tests;

internal static class Program
{
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
