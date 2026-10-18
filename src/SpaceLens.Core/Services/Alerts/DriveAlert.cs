namespace SpaceLens.Core.Services.Alerts;

/// <summary>Something the daily check wants the user to know, in words they can act on.</summary>
public sealed record DriveAlert(string Title, string Message);
