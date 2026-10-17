using System.IO;
using System.Text;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Services.Security;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Security;

public sealed class WindowsCredentialStoreTests
{
    private const string Key = "sk-ant-api03-ThisIsNotARealKey_0123456789abcdef";

    [Fact]
    public async Task SavedKeyRoundTripsButIsNeverStoredInPlainText()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Path);
        var store = new WindowsCredentialStore(paths);

        await store.SaveApiKeyAsync("  " + Key + "\n");

        Assert.True(store.HasApiKey);
        Assert.Equal(Key, await store.LoadApiKeyAsync());
        Assert.Equal("cdef", store.ApiKeyHint);
        var onDisk = await File.ReadAllBytesAsync(paths.CredentialPath);
        Assert.DoesNotContain("ThisIsNotARealKey", Encoding.UTF8.GetString(onDisk));
        Assert.DoesNotContain("ThisIsNotARealKey", Encoding.Unicode.GetString(onDisk));
    }

    [Fact]
    public async Task RemovedKeyIsGone()
    {
        using var folder = new TestFolder();
        var store = new WindowsCredentialStore(new AppPaths(folder.Path));
        await store.SaveApiKeyAsync(Key);

        store.RemoveApiKey();

        Assert.False(store.HasApiKey);
        Assert.Null(await store.LoadApiKeyAsync());
        Assert.Null(store.ApiKeyHint);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("sk-ant-has spaces inside the key")]
    [InlineData("sk-ant-line\nbreak-0000000000")]
    public async Task ImplausibleKeysAreRejected(string key)
    {
        using var folder = new TestFolder();
        var store = new WindowsCredentialStore(new AppPaths(folder.Path));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveApiKeyAsync(key));
        Assert.False(store.HasApiKey);
    }

    [Fact]
    public async Task CorruptBlobGivesAnActionableError()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Path);
        await File.WriteAllBytesAsync(paths.CredentialPath, [1, 2, 3, 4]);
        var store = new WindowsCredentialStore(paths);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadApiKeyAsync());
        Assert.Contains("save it again", error.Message);
    }
}
