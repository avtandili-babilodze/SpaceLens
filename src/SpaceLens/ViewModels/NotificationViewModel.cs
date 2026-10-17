using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>The toast at the bottom of the window. Any view model shows one by sending a <see cref="NotificationMessage"/>.</summary>
public sealed partial class NotificationViewModel : ObservableObject, IRecipient<NotificationMessage>
{
    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(12);

    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _hideTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private string? _text;

    [ObservableProperty]
    private bool _isError;

    public NotificationViewModel(IMessenger messenger, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        messenger.Register(this);
    }

    public bool IsVisible => Text is not null;

    public void Receive(NotificationMessage message) => Show(message.Text, message.IsError);

    public void Show(string text, bool isError = false)
    {
        _hideTimer?.Cancel();
        _hideTimer = new CancellationTokenSource();
        Text = text;
        IsError = isError;
        _ = HideAfterAsync(isError ? ErrorDuration : InfoDuration, _hideTimer.Token);
    }

    [RelayCommand]
    private void Dismiss()
    {
        _hideTimer?.Cancel();
        Text = null;
    }

    private async Task HideAfterAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, cancellationToken);
            Text = null;
        }
        catch (OperationCanceledException)
        {
            // A newer notification replaced this one.
        }
    }
}
