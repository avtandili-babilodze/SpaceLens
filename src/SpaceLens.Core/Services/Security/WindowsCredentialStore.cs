using System.Security.Cryptography;
using System.Text;
using SpaceLens.Core.Configuration;

namespace SpaceLens.Core.Services.Security;

/// <summary>
/// Stores the Anthropic API key encrypted with Windows DPAPI for the current Windows user.
/// Only this user on this machine can decrypt it; the plaintext is never written to disk.
/// </summary>
public sealed class WindowsCredentialStore(AppPaths paths) : ICredentialStore
{
    // Additional entropy ties the blob to SpaceLens, so another DPAPI consumer can't decrypt it by accident.
    private static readonly byte[] Entropy = "SpaceLens.AnthropicApiKey.v1"u8.ToArray();

    private string? _hint;

    public bool HasApiKey => File.Exists(paths.CredentialPath);

    public string? ApiKeyHint => HasApiKey ? _hint ??= ReadHint() : null;

    public async Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var key = apiKey.Trim();
        if (!IsPlausibleKey(key))
        {
            const string Message = "That doesn't look like an Anthropic API key. "
                + "Paste the whole key on one line (it starts with \"sk-ant-\").";
            throw new ArgumentException(Message);
        }

        var plaintext = Encoding.UTF8.GetBytes(key);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            var temporaryPath = paths.CredentialPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken);
            File.Move(temporaryPath, paths.CredentialPath, overwrite: true);
            _hint = key[^4..];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<string?> LoadApiKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!HasApiKey)
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(paths.CredentialPath, cancellationToken);
        return Decrypt(encrypted);
    }

    public void RemoveApiKey()
    {
        File.Delete(paths.CredentialPath);
        _hint = null;
    }

    internal static bool IsPlausibleKey(string key) =>
        key.Length is >= 20 and <= 512 && key.All(c => c is > ' ' and < (char)127);

    private static string Decrypt(byte[] encrypted)
    {
        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            const string Message = "The saved API key can't be unlocked by this Windows account. "
                + "Remove it in Settings and save it again.";
            throw new InvalidOperationException(Message);
        }

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private string? ReadHint()
    {
        try
        {
            var key = Decrypt(File.ReadAllBytes(paths.CredentialPath));
            return key.Length >= 4 ? key[^4..] : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
