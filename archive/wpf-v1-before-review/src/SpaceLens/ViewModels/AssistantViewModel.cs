using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Models;
using SpaceLens.Services.AI;

namespace SpaceLens.ViewModels;

public sealed partial class AssistantViewModel(IAiAssistantService claude, LocalStorageAssistant local, AiContextBuilder contextBuilder, SettingsViewModel settings) : ObservableObject
{
    public StorageSnapshot? Snapshot { get; set; }
    public FileEntry? CurrentFolder { get; set; }
    public SnapshotComparison? Comparison { get; set; }
    public ObservableCollection<ChatMessage> Messages { get; } = [new("local", "Scan a drive or folder to understand what is taking up space. Offline analysis keeps your metadata on this device.")];
    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool isExpanded = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string contextLabel = "No scan selected";
    public event Func<long, Task>? ReferenceRequested;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken token)
    {
        string question = Input.Trim(); if (question.Length == 0) return;
        if (question.Length > 4000) { Messages.Add(new("error", "Please keep the question under 4,000 characters.")); return; }
        Input = ""; var history = Messages.ToArray(); Messages.Add(new("user", question) { WasSharedWithCloud = settings.UseClaude && settings.ShareMetadata });
        if (Snapshot is null || CurrentFolder is null) { Messages.Add(new("local", "No completed scan is available. Select a drive or folder and scan it first.")); return; }
        IsBusy = true;
        try
        {
            AiReply reply; string role;
            if (settings.UseClaude)
            {
                if (!settings.ShareMetadata) { Messages.Add(new("error", "Metadata sharing is off. In Settings, explicitly consent to sending your question, recent conversation, and a small selection of paths and sizes to Anthropic, or switch to offline analysis.")); return; }
                var context = await contextBuilder.BuildAsync(Snapshot, CurrentFolder, question, Comparison);
                token.ThrowIfCancellationRequested();
                if (!settings.ShareMetadata || !settings.UseClaude) throw new InvalidOperationException("Cloud sharing was disabled before the request was sent. Use offline analysis or consent again in Settings.");
                reply = await claude.AskAsync(question, context, history, token); role = "assistant";
            }
            else { reply = await local.AskAsync(question, Snapshot, CurrentFolder, Comparison); role = "local"; }
            Messages.Add(new(role, reply.Text, reply.References));
        }
        catch (OperationCanceledException) { Messages.Add(new("local", "Request cancelled.")); }
        catch (Exception ex) { Messages.Add(new("error", ex.Message)); }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;
    [RelayCommand] private async Task LocateAsync(AiReference? reference) { if (reference is not null && ReferenceRequested is not null) await ReferenceRequested(reference.EntryId); }
    [RelayCommand] private void Clear() { Messages.Clear(); Messages.Add(new("local", "Conversation cleared. Your scan and saved snapshots are unchanged.")); }
}
