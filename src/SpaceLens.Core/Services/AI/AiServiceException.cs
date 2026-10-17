namespace SpaceLens.Core.Services.AI;

/// <summary>A failure talking to Claude, with a message that is safe to show (it never contains the API key).</summary>
public sealed class AiServiceException(AiErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public AiErrorKind Kind { get; } = kind;

    /// <summary>How long Anthropic asked us to wait (the retry-after header), when it said.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public bool IsRetryable => Kind is AiErrorKind.RateLimited or AiErrorKind.Overloaded or AiErrorKind.ServerError;
}
