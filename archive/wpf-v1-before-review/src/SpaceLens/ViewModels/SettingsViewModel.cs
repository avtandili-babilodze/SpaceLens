using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Infrastructure;
using SpaceLens.Services.AI;
using SpaceLens.Services.Security;

namespace SpaceLens.ViewModels;

public sealed partial class SettingsViewModel(ICredentialStore credentials, IAiAssistantService claude, AppPaths paths) : ObservableObject
{
    [ObservableProperty] private bool hasKey = credentials.HasCredential;
    [ObservableProperty] private bool shareMetadata;
    [ObservableProperty] private bool useClaude;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string connectionStatus = credentials.HasCredential ? "Key saved · connection not tested" : "Offline · no API key saved";
    public string DataDirectory => paths.Root;
    public string Model => ClaudeHaikuService.Model;
    public event Action? KeySaved;
    public async Task SaveKeyAsync(string key)
    {
        IsBusy = true;
        try { await credentials.SaveAsync(key); HasKey = true; ConnectionStatus = "Key encrypted for this Windows user. Test the connection to verify access."; KeySaved?.Invoke(); }
        catch (Exception ex) { ConnectionStatus = ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestConnectionAsync(CancellationToken token)
    {
        IsBusy = true; ConnectionStatus = "Connecting to Anthropic…";
        try { await claude.TestConnectionAsync(token); ConnectionStatus = "Connected · Claude Haiku 5.5 responded successfully"; }
        catch (OperationCanceledException) { ConnectionStatus = "Connection test cancelled."; }
        catch (Exception ex) { ConnectionStatus = ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    private async Task RemoveKeyAsync()
    {
        try { await credentials.RemoveAsync(); HasKey = false; UseClaude = false; ShareMetadata = false; ConnectionStatus = "Key removed. Offline analysis is available."; }
        catch (Exception ex) { ConnectionStatus = ex.Message; }
    }
}
