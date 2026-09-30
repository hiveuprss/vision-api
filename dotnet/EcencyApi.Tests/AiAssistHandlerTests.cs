using System.Text.Json.Nodes;
using EcencyApi.Handlers;
using Xunit;

namespace EcencyApi.Tests;

/// <summary>
/// AI assist bills Points. The SDK sends an idempotency_key so a retry or a
/// double submit returns the cached result instead of charging again. The
/// proxy has to forward that key; older clients omit it, and those requests
/// must still go through.
/// </summary>
[Collection("ai-assist")]
public class AiAssistHandlerTests : IDisposable
{
    private readonly CurationDeskTestSupport.Recorder _upstream = new();

    public AiAssistHandlerTests()
    {
        // `code` "as:alice" validates as alice; anything else is invalid.
        PrivateApi.AiAssistValidateCode = body =>
        {
            var code = body["code"]?.GetValue<string>();
            return Task.FromResult(code != null && code.StartsWith("as:", StringComparison.Ordinal) ? code[3..] : null);
        };
        PrivateApi.AiAssistUpstream = (endpoint, method, payload) =>
            _upstream.Handle(endpoint, method, Array.Empty<KeyValuePair<string, string>>(), payload);
    }

    public void Dispose()
    {
        PrivateApi.AiAssistValidateCode = PrivateApi.ValidateCode;
        PrivateApi.AiAssistUpstream = (endpoint, method, payload) =>
            EcencyApi.Infrastructure.ApiClient.ApiRequest(endpoint, method, null, payload, null, 120000);
    }

    [Fact]
    public async Task ForwardsTheIdempotencyKeyWithTheAssistBody()
    {
        var ctx = CurationDeskTestSupport.Post(
            "/private-api/ai-assist",
            """{"code":"as:alice","action":"improve","text":"hello","idempotency_key":"retry-key-1"}""");
        await PrivateApi.AiAssist(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
        var call = Assert.Single(_upstream.Calls);
        Assert.Equal("ai-assist", call.Endpoint);
        Assert.Equal(HttpMethod.Post, call.Method);
        var payload = Assert.IsType<JsonObject>(call.Payload);
        Assert.Equal("alice", payload["us"]?.GetValue<string>());
        Assert.Equal("improve", payload["action"]?.GetValue<string>());
        Assert.Equal("hello", payload["text"]?.GetValue<string>());
        Assert.Equal("retry-key-1", payload["idempotency_key"]?.GetValue<string>());
    }

    [Fact]
    public async Task OmitsTheIdempotencyKeyWhenTheClientDoesNotSendOne()
    {
        var ctx = CurationDeskTestSupport.Post(
            "/private-api/ai-assist",
            """{"code":"as:alice","action":"improve","text":"hello"}""");
        await PrivateApi.AiAssist(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
        var call = Assert.Single(_upstream.Calls);
        var payload = Assert.IsType<JsonObject>(call.Payload);
        Assert.Equal("alice", payload["us"]?.GetValue<string>());
        Assert.Equal("improve", payload["action"]?.GetValue<string>());
        Assert.Equal("hello", payload["text"]?.GetValue<string>());
        Assert.False(payload.ContainsKey("idempotency_key"));
    }

    [Fact]
    public async Task AnInvalidCodeIsUnauthorizedAndReachesNothing()
    {
        var ctx = CurationDeskTestSupport.Post(
            "/private-api/ai-assist",
            """{"code":"bogus","action":"improve","text":"hello","idempotency_key":"retry-key-1"}""");
        await PrivateApi.AiAssist(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.Empty(_upstream.Calls);
    }
}

[CollectionDefinition("ai-assist", DisableParallelization = true)]
public class AiAssistCollection { }
