using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Sdk.Models;

namespace OpenCode.Sdk.Tests.Support;

/// <summary>Builds the small JSON inputs used by the live RPC contract proofs.</summary>
internal sealed class RpcCallRequestBuilder
{
    private readonly JsonObject _input = [];

    public RpcCallRequestBuilder WithNonce(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        _input["nonce"] = JsonValue.Create(nonce);
        return this;
    }

    public RpcCallRequestBuilder WithValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        _input["value"] = JsonValue.Create(value);
        return this;
    }

    public RpcCallRequestBuilder WithNumericNonce(int nonce)
    {
        _input["nonce"] = JsonValue.Create(nonce);
        return this;
    }

    public RpcCallPostRequest Build()
    {
        using var document = JsonDocument.Parse(_input.ToJsonString());
        return new RpcCallPostRequest { Input = document.RootElement.Clone() };
    }
}
