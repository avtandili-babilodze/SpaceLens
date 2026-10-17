namespace SpaceLens.Core.Services.Security;

public interface ICredentialStore
{
    bool HasApiKey { get; }

    /// <summary>The last four characters of the saved key, for display ("…a1B2"); null when none is saved.</summary>
    string? ApiKeyHint { get; }

    Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>The decrypted key, or null when none is saved.</summary>
    Task<string?> LoadApiKeyAsync(CancellationToken cancellationToken = default);

    void RemoveApiKey();
}
