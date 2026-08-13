using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenCode.Sdk.Tests")]

// Benchmarks measure the production serialization dispatch, not a reflection stand-in.
[assembly: InternalsVisibleTo("OpenCode.Sdk.Performance.Tests")]

// NSubstitute proxies internal seam interfaces through Castle DynamicProxy.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
