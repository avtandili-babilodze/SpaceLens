namespace SpaceLens.Core.Services.AI;

public enum AiErrorKind
{
    NotConfigured,
    Authentication,
    Permission,
    Billing,
    ModelUnavailable,
    RateLimited,
    Overloaded,
    ServerError,
    InvalidRequest,
    Network,
    Timeout,
    Refused,
    Unexpected
}
