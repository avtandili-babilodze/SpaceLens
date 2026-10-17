using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Services.Security;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// Thin client for POST https://api.anthropic.com/v1/messages: authentication, timeouts,
/// retries for rate limits and overload, and error messages a user can act on.
/// Logs status codes and retry decisions only, never the key or the request body.
/// </summary>
public sealed partial class AnthropicMessagesClient(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentials,
    ILogger<AnthropicMessagesClient> logger) : IAnthropicMessagesClient
{
    /// <summary>Name of the HttpClient registration; timeouts are applied per request instead of on the client.</summary>
    public const string HttpClientName = "Anthropic";

    public const string ApiVersion = "2023-06-01";

    public static readonly Uri Endpoint = new("https://api.anthropic.com/v1/messages");

    private const int MaxAttempts = 3;
    private const string NotConfiguredMessage = "Add your Anthropic API key in Settings › AI Assistant to use Claude.";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    public async Task<JsonObject> CreateMessageAsync(JsonObject body, CancellationToken cancellationToken)
    {
        var apiKey = await credentials.LoadApiKeyAsync(cancellationToken)
            ?? throw new AiServiceException(AiErrorKind.NotConfigured, NotConfiguredMessage);
        var json = body.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(json, apiKey, cancellationToken);
            }
            catch (AiServiceException exception) when (CanRetry(exception, attempt) && RetryDelay(exception, attempt) is { } delay)
            {
                logger.LogWarning(
                    "Anthropic request failed ({Kind}); retrying in {Delay} (attempt {Attempt} of {MaxAttempts})",
                    exception.Kind,
                    delay,
                    attempt,
                    MaxAttempts);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    internal static AiServiceException MapError(HttpStatusCode status, string responseBody)
    {
        var (type, message) = ReadError(responseBody);
        var mentionsCredits = message.Contains("credit balance", StringComparison.OrdinalIgnoreCase);

        return (status, type) switch
        {
            (HttpStatusCode.Unauthorized, _) or (_, "authentication_error") => new(AiErrorKind.Authentication,
                "Anthropic didn't accept the API key (401). Check the key in Settings or create a new one in the Anthropic Console."),
            (HttpStatusCode.PaymentRequired, _) or (_, "billing_error") => new(AiErrorKind.Billing,
                "Your Anthropic account has a billing problem (402). Check Plans & Billing in the Anthropic Console."),
            (HttpStatusCode.BadRequest, _) when mentionsCredits => new(AiErrorKind.Billing,
                "Your Anthropic credit balance is too low. Add credits under Plans & Billing in the Anthropic Console."),
            (HttpStatusCode.Forbidden, _) or (_, "permission_error") => new(AiErrorKind.Permission,
                "This API key isn't allowed to make this request (403). Check the key's workspace permissions."),
            (HttpStatusCode.NotFound, _) or (_, "not_found_error") => new(AiErrorKind.ModelUnavailable,
                "Claude Haiku 5.5 isn't available to this API key (404)."),
            ((HttpStatusCode)429, _) or (_, "rate_limit_error") => new(AiErrorKind.RateLimited,
                "Anthropic's rate limit was reached (429). Wait a moment and try again."),
            ((HttpStatusCode)529, _) or (_, "overloaded_error") => new(AiErrorKind.Overloaded,
                "Claude is overloaded right now (529). Try again in a minute."),
            _ when (int)status >= 500 => new(AiErrorKind.ServerError,
                $"Anthropic had a temporary problem ({(int)status}). Try again in a minute."),
            _ => new(AiErrorKind.InvalidRequest,
                $"Anthropic couldn't process the request ({(int)status}){(message.Length > 0 ? ": " + Sanitize(message) : ".")}")
        };
    }

    private async Task<JsonObject> SendOnceAsync(string json, string apiKey, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        try
        {
            using var http = httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.SendAsync(request, timeout.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Anthropic returned HTTP {Status}", (int)response.StatusCode);
                var error = MapError(response.StatusCode, responseBody);
                throw new AiServiceException(error.Kind, error.Message) { RetryAfter = response.Headers.RetryAfter?.Delta };
            }

            return JsonNode.Parse(responseBody) as JsonObject
                ?? throw new AiServiceException(AiErrorKind.Unexpected, "Anthropic returned an empty response. Try again.");
        }
        catch (HttpRequestException exception)
        {
            const string Message = "Couldn't reach Anthropic. Check your internet connection; "
                + "everything else in SpaceLens works offline.";
            throw new AiServiceException(AiErrorKind.Network, Message, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceException(AiErrorKind.Timeout, "Claude took too long to answer. Try again.", exception);
        }
        catch (JsonException exception)
        {
            const string Message = "Anthropic returned a response SpaceLens couldn't read. Try again.";
            throw new AiServiceException(AiErrorKind.Unexpected, Message, exception);
        }
    }

    /// <summary>Waits what Anthropic asked for (or 1 s, then 3 s); gives up when it asks for longer than a user would wait.</summary>
    private static bool CanRetry(AiServiceException exception, int attempt) => exception.IsRetryable && attempt < MaxAttempts;

    private static TimeSpan? RetryDelay(AiServiceException exception, int attempt)
    {
        var delay = exception.RetryAfter ?? TimeSpan.FromSeconds(Math.Pow(3, attempt - 1));
        return delay <= MaxRetryDelay ? delay : null;
    }

    private static (string Type, string Message) ReadError(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            return (error?["type"]?.GetValue<string>() ?? "", error?["message"]?.GetValue<string>() ?? "");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return ("", "");
        }
    }

    /// <summary>Error text is shown in the UI, so strip anything that looks like a key and keep it short.</summary>
    private static string Sanitize(string message)
    {
        var redacted = KeyPattern().Replace(message, "[redacted]");
        return redacted.Length <= 200 ? redacted : redacted[..200] + "…";
    }

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]+")]
    private static partial Regex KeyPattern();
}
