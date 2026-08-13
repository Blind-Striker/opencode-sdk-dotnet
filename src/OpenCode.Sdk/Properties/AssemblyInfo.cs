using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenCode.Sdk.Tests")]

// NSubstitute proxies internal seam interfaces through Castle DynamicProxy.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
