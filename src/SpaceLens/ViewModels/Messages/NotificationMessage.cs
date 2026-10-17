namespace SpaceLens.ViewModels.Messages;

/// <summary>A short confirmation shown at the bottom of the window.</summary>
public sealed record NotificationMessage(string Text, bool IsError = false);
