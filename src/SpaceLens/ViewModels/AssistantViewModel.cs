using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Security;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Settings;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>
/// The storage assistant. With an API key and the user's consent it asks Claude Haiku 5.5; otherwise
/// (or when Claude can't be reached) it answers offline from the scan.
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject,
    IRecipient<AskAssistantMessage>,
    IRecipient<AiConfigurationChangedMessage>
{
    /// <summary>Long conversations resend a lot of history; start fresh after this many questions.</summary>
    private const int MaxQuestionsPerConversation = 12;

    private readonly IAiAssistantService _claude;
    private readonly ILocalStorageAssistant _local;
    private readonly ICredentialStore _credentials;
    private readonly ISettingsStore _settings;
    private readonly IScanSession _session;
    private readonly IMessenger _messenger;
    private AiConversation _conversation = new();
    private CancellationTokenSource? _answerCancellation;
    private Task _answering = Task.CompletedTask;
    private int _latestRequest;
    private string? _pendingQuestion;
    private bool _offlineHintShown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuggestions))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _activity;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isOpen = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuggestions))]
    private bool _needsConsent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusText))]
    private StorageNode? _focus;

    public AssistantViewModel(
        IAiAssistantService claude,
        ILocalStorageAssistant local,
        ICredentialStore credentials,
        ISettingsStore settings,
        IScanSession session,
        IMessenger messenger)
    {
        _claude = claude;
        _local = local;
        _credentials = credentials;
        _settings = settings;
        _session = session;
        _messenger = messenger;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowSuggestions));
        settings.Changed += (_, _) => OnPropertyChanged(nameof(ModeLabel));
        session.ScanLoaded += (_, _) => OnScanLoaded();
        messenger.RegisterAll(this);
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public IReadOnlyList<string> Suggestions { get; } =
    [
        "Why is my drive full?",
        "What can I clean without breaking anything?",
        "Free up 20 GB safely",
        "What grew since my last scan?"
    ];

    public bool ShowSuggestions => Messages.Count == 0 && !IsBusy && !NeedsConsent;

    public bool UsesClaude => _credentials.HasApiKey && _settings.Current.ShareScanDetails;

    public string ModeLabel => UsesClaude ? "Claude Haiku 5.5" : _credentials.HasApiKey ? "Waiting for consent" : "Offline answers";

    public string FocusText => Focus is null ? "Whole scan" : NodePresenter.GetDisplayName(Focus, _session.Current?.Drive);

    public void Receive(AskAssistantMessage message)
    {
        IsOpen = true;
        IsExpanded = true;
        Focus = message.Focus ?? Focus;
        _ = SendAsync(message.Question);
    }

    public void Receive(AiConfigurationChangedMessage message) => OnPropertyChanged(nameof(ModeLabel));

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync(string? question)
    {
        var text = (question ?? Input).Trim();
        if (text.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (question is null)
        {
            Input = "";
        }

        AddMessage(ChatRole.User, text);
        if (!_session.HasScan)
        {
            AddMessage(ChatRole.Notice, "Scan a drive or folder first, and I'll answer from what's actually on it.");
            return Task.CompletedTask;
        }

        if (_credentials.HasApiKey && !_settings.Current.ShareScanDetails)
        {
            _pendingQuestion = text;
            NeedsConsent = true;
            return Task.CompletedTask;
        }

        return AnswerAsync(text, forceOffline: false);
    }

    private bool CanSend(string? question) => !IsBusy && (question is not null || !string.IsNullOrWhiteSpace(Input));

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _answerCancellation?.Cancel();

    [RelayCommand]
    private Task AllowSharingAsync()
    {
        _settings.Save(_settings.Current with { ShareScanDetails = true });
        NeedsConsent = false;
        return AnswerPendingAsync(forceOffline: false);
    }

    [RelayCommand]
    private Task AnswerOfflineAsync()
    {
        NeedsConsent = false;
        return AnswerPendingAsync(forceOffline: true);
    }

    [RelayCommand]
    private void Clear()
    {
        _answerCancellation?.Cancel();
        _conversation = new AiConversation();
        Messages.Clear();
        NeedsConsent = false;
        _pendingQuestion = null;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
        IsExpanded = true;
    }

    [RelayCommand]
    private void OpenLink(string? path)
    {
        if (path is not null && _session.Current?.Root.Find(path) is { } node)
        {
            _messenger.Send(new ShowInExplorerMessage(node));
        }
        else
        {
            _messenger.Send(new NotificationMessage("That item isn't part of the current scan.", IsError: true));
        }
    }

    [RelayCommand]
    private void OpenPlan(ChatMessageViewModel? message)
    {
        if (message is { GoalBytes: > 0 })
        {
            _messenger.Send(new OpenCleanupPlanMessage(message.GoalBytes));
        }
    }

    private Task AnswerPendingAsync(bool forceOffline)
    {
        var question = _pendingQuestion;
        _pendingQuestion = null;
        OnPropertyChanged(nameof(ModeLabel));
        return question is null ? Task.CompletedTask : AnswerAsync(question, forceOffline);
    }

    /// <summary>
    /// Starts an answer after any earlier one has stopped, so two answers never share the conversation.
    /// A new question cancels the one still running.
    /// </summary>
    private Task AnswerAsync(string question, bool forceOffline)
    {
        _answerCancellation?.Cancel();
        _answerCancellation = new CancellationTokenSource();
        var request = ++_latestRequest;
        _answering = RunAfterAsync(_answering, question, forceOffline, request, _answerCancellation.Token);
        return _answering;
    }

    private async Task RunAfterAsync(Task previous, string question, bool forceOffline, int request, CancellationToken token)
    {
        // Only wait for the previous answer to finish; its errors were already reported (and must not repeat here).
        await Task.WhenAny(previous);
        var useClaude = UsesClaude && !forceOffline;

        IsBusy = true;
        Activity = useClaude ? "Thinking…" : "Looking at your scan…";
        try
        {
            token.ThrowIfCancellationRequested();
            if (useClaude)
            {
                await AskClaudeAsync(question, token);
            }
            else
            {
                await AnswerLocallyAsync(question, token);
                ShowOfflineHintOnce();
            }
        }
        catch (OperationCanceledException)
        {
            AddMessage(ChatRole.Notice, "Stopped.");
        }
        catch (AiServiceException exception) when (exception.Kind is AiErrorKind.Network or AiErrorKind.Timeout
            or AiErrorKind.Overloaded or AiErrorKind.ServerError or AiErrorKind.RateLimited)
        {
            AddMessage(ChatRole.Error, exception.Message + " Here's an offline answer instead.");
            await AnswerLocallyAsync(question, CancellationToken.None);
        }
        catch (Exception exception) when (exception is AiServiceException or InvalidOperationException or IOException)
        {
            AddMessage(ChatRole.Error, exception.Message);
        }
        finally
        {
            // Only the newest request decides whether the panel still looks busy.
            if (request == _latestRequest)
            {
                IsBusy = false;
                Activity = null;
            }
        }
    }

    private async Task AskClaudeAsync(string question, CancellationToken token)
    {
        if (_conversation.QuestionCount >= MaxQuestionsPerConversation)
        {
            _conversation = new AiConversation();
            AddMessage(ChatRole.Notice, "Started a fresh conversation to keep answers quick.");
        }

        var activity = new Progress<string>(status => Activity = status + "…");
        var reply = await _claude.AskAsync(_conversation, question, Focus, activity, token);
        Messages.Add(new ChatMessageViewModel(ChatRole.Assistant, reply.Text, AnswerSource.Claude, reply.Plan));
    }

    private async Task AnswerLocallyAsync(string question, CancellationToken token)
    {
        var reply = await _local.AskAsync(question, Focus, token);
        Messages.Add(new ChatMessageViewModel(ChatRole.Assistant, reply.Text, AnswerSource.Offline, reply.Plan));
    }

    private void ShowOfflineHintOnce()
    {
        if (_credentials.HasApiKey || _offlineHintShown)
        {
            return;
        }

        _offlineHintShown = true;
        AddMessage(ChatRole.Notice, "Answered offline from your scan. Add an Anthropic API key in Settings to ask Claude instead.");
    }

    private void OnScanLoaded()
    {
        _answerCancellation?.Cancel();
        _conversation = new AiConversation();
        Focus = null;
        if (Messages.Count > 0)
        {
            AddMessage(ChatRole.Notice, "A new scan is loaded. Answers now use it.");
        }
    }

    private void AddMessage(ChatRole role, string text) => Messages.Add(new ChatMessageViewModel(role, text));
}
