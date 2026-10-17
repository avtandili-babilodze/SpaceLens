namespace SpaceLens.Core.Repositories;

/// <summary>The scan history database couldn't be read or written. The message is safe to show to the user.</summary>
public sealed class HistoryStoreException(string message, Exception innerException) : Exception(message, innerException);
