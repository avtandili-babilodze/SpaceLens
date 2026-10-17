namespace SpaceLens.ViewModels;

/// <summary>Where the Settings page is with the Anthropic API key.</summary>
public enum ConnectionState
{
    NoKey,
    NotTested,
    Testing,
    Connected,
    Failed
}
