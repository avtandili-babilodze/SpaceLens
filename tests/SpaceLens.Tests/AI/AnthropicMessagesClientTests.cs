using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using SpaceLens.Core.Services.AI;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.AI;

public sealed class AnthropicMessagesClientTests
{
    private const string Ok = """{"type":"message","content":[{"type":"text","text":"OK"}],"stop_reason":"end_turn"}""";

    private static JsonObject Body() => new() { ["model"] = "claude-haiku-5-5", ["max_tokens"] = 16 };

    [Fact]
    public async Task SendsTheKeyAndVersionHeadersToTheMessagesEndpoint()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Ok));
        var client = TestServices.AnthropicClient(handler, new MemoryCredentialStore("sk-ant-api03-test-key-0000000000"));

        var response = await client.CreateMessageAsync(Body(), CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.AbsoluteUri);
        Assert.Equal("sk-ant-api03-test-key-0000000000", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("claude-haiku-5-5", JsonNode.Parse(body)!["model"]!.GetValue<string>());
        Assert.Equal("message", response["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(401, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", AiErrorKind.Authentication)]
    [InlineData(402, """{"type":"error","error":{"type":"billing_error","message":"billing"}}""", AiErrorKind.Billing)]
    [InlineData(400, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low"}}""", AiErrorKind.Billing)]
    [InlineData(403, """{"type":"error","error":{"type":"permission_error","message":"no"}}""", AiErrorKind.Permission)]
    [InlineData(404, """{"type":"error","error":{"type":"not_found_error","message":"model: claude-haiku-5-5"}}""", AiErrorKind.ModelUnavailable)]
    [InlineData(400, """{"type":"error","error":{"type":"invalid_request_error","message":"messages: bad"}}""", AiErrorKind.InvalidRequest)]
    public async Task MapsApiErrorsToActionableKinds(int status, string body, AiErrorKind expected)
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json((HttpStatusCode)status, body));
        var client = TestServices.AnthropicClient(handler);

        var error = await Assert.ThrowsAsync<AiServiceException>(() => client.CreateMessageAsync(Body(), CancellationToken.None));

        Assert.Equal(expected, error.Kind);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(429, AiErrorKind.RateLimited)]
    [InlineData(529, AiErrorKind.Overloaded)]
    [InlineData(500, AiErrorKind.ServerError)]
    public async Task RetriesTransientFailuresThenReportsThem(int status, AiErrorKind expected)
    {
        var handler = new StubHttpHandler((_, _) =>
        {
            var response = StubHttpHandler.Json((HttpStatusCode)status, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });
        var client = TestServices.AnthropicClient(handler);

        var error = await Assert.ThrowsAsync<AiServiceException>(() => client.CreateMessageAsync(Body(), CancellationToken.None));

        Assert.Equal(expected, error.Kind);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task RecoversWhenARetrySucceeds()
    {
        var handler = new StubHttpHandler(
            (_, _) =>
            {
                var limited = StubHttpHandler.Json((HttpStatusCode)429, "{}");
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            },
            (_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Ok));
        var client = TestServices.AnthropicClient(handler);

        var response = await client.CreateMessageAsync(Body(), CancellationToken.None);

        Assert.Equal("message", response["type"]!.GetValue<string>());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailuresAreReportedWithoutInternalDetails()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("socket 10.0.0.1:443 refused"));
        var client = TestServices.AnthropicClient(handler);

        var error = await Assert.ThrowsAsync<AiServiceException>(() => client.CreateMessageAsync(Body(), CancellationToken.None));

        Assert.Equal(AiErrorKind.Network, error.Kind);
        Assert.DoesNotContain("10.0.0.1", error.Message);
    }

    [Fact]
    public async Task MissingKeyIsReportedBeforeAnyRequest()
    {
        var handler = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.OK, Ok));
        var client = TestServices.AnthropicClient(handler, new MemoryCredentialStore(key: null));

        var error = await Assert.ThrowsAsync<AiServiceException>(() => client.CreateMessageAsync(Body(), CancellationToken.None));

        Assert.Equal(AiErrorKind.NotConfigured, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ErrorMessagesNeverEchoAKey()
    {
        var error = AnthropicMessagesClient.MapError(
            HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"bad header sk-ant-api03-SECRETSECRET"}}""");

        Assert.DoesNotContain("SECRET", error.Message);
    }
}
