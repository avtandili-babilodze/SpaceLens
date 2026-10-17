using System.IO;
using System.Security.Cryptography;
using System.Text;
using SpaceLens.Infrastructure;

namespace SpaceLens.Services.Security;

public interface ICredentialStore
{
    bool HasCredential { get; }
    Task SaveAsync(string apiKey);
    Task<string?> LoadAsync();
    Task RemoveAsync();
}

public sealed class WindowsCredentialStore(AppPaths paths) : ICredentialStore
{
    public bool HasCredential => File.Exists(paths.CredentialPath);
    public async Task SaveAsync(string apiKey)
    {
        string key = apiKey.Trim();
        if (key.Length < 8 || key.Contains('\r') || key.Contains('\n')) throw new ArgumentException("Paste a valid API key on a single line.");
        byte[] clear = Encoding.UTF8.GetBytes(key);
        try
        {
            byte[] encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            string temporary = paths.CredentialPath + ".tmp";
            await File.WriteAllBytesAsync(temporary, encrypted);
            File.Move(temporary, paths.CredentialPath, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public async Task<string?> LoadAsync()
    {
        if (!HasCredential) return null;
        byte[] encrypted = await File.ReadAllBytesAsync(paths.CredentialPath);
        byte[] clear;
        try { clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser); }
        catch (CryptographicException) { throw new InvalidOperationException("The saved key cannot be unlocked by this Windows user. Remove it and save a new key."); }
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public Task RemoveAsync()
    {
        if (File.Exists(paths.CredentialPath)) File.Delete(paths.CredentialPath);
        return Task.CompletedTask;
    }
}
