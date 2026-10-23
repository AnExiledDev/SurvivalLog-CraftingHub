namespace CraftingHub.Core;

/// <summary>
/// Says when a value read from the game is due again: at once the first time, then at most once per interval. The
/// first read has no time to subtract from, so it is kept as a flag rather than a "long ago" time that would overflow.
/// </summary>
public sealed class ReadThrottle
{
    private readonly long _intervalMs;
    private long? _lastReadAt;

    public ReadThrottle(long intervalMs) => _intervalMs = intervalMs;

    /// <summary>True when a read is due at <paramref name="now"/> (milliseconds), and counts it as the read.</summary>
    public bool IsDue(long now)
    {
        if (_lastReadAt is { } last && now - last < _intervalMs) return false;

        _lastReadAt = now;

        return true;
    }
}
