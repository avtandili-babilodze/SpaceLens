using SpaceLens.Core.Services.Security;

namespace SpaceLens.Tests.Support;

public sealed class MemoryCredentialStore(string? key = "sk-ant-api03-test-key-not-real-0000") : ICredentialStore
{
    public string? Key { get; private set; } = key;

    public bool HasApiKey => Key is not null;

    public string? ApiKeyHint => Key?[^4..];

    public Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        Key = apiKey;
        return Task.CompletedTask;
    }

    public Task<string?> LoadApiKeyAsync(CancellationToken cancellationToken = default) => Task.FromResult(Key);

    public void RemoveApiKey() => Key = null;
}
