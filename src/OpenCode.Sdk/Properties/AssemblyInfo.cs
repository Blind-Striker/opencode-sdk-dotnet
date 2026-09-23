using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenCode.Sdk.Tests")]

// The isolated service process records the contenders its Ensure election starts, so the launching
// test can end every one of them.
[assembly: InternalsVisibleTo("OpenCode.Sdk.ServiceFixture")]

// Benchmarks measure the production serialization dispatch, not a reflection stand-in.
[assembly: InternalsVisibleTo("OpenCode.Sdk.Performance.Tests")]

// NSubstitute proxies internal seam interfaces through Castle DynamicProxy.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
