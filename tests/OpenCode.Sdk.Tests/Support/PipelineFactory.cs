using OpenCode.Sdk.Internal;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Builds a pipeline over a test transport; the endpoint every pipeline test shares.</summary>
internal static class PipelineFactory
{
    public static readonly Uri Endpoint = new("http://localhost:4096");

    public static Pipeline Create(
        HttpClient httpClient,
        bool ownsHttpClient = false,
        Uri? endpoint = null,
        string? password = null,
        string? username = null,
        LocationSelector? location = null)
    {
        var options = new OpenCodeClientOptions
        {
            Endpoint = endpoint ?? Endpoint,
            Password = password,
            Location = location,
        };
        if (username is not null)
        {
            options.Username = username;
        }

        return new Pipeline(httpClient, ownsHttpClient, options);
    }
}
