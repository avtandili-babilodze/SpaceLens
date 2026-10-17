namespace SpaceLens.Tests.Support;

/// <summary>A clock that always reads the same time, so age-based rules are deterministic.</summary>
public sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
