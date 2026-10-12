namespace CraftingHub.Core;

/// <summary>
/// When to check the keep-stocked targets: once each time a workbench opens, after the game has had a moment to fill
/// in its recipe list, and again when the player changes a target. Never on a timer and never after a run ends, so a
/// craft that keeps failing cannot turn into a loop of top-ups.
/// </summary>
public sealed class TopUpTiming
{
    public const double SettleSeconds = 1.5;

    private bool _wasOpen;
    private double _openedAt;
    private bool _isDue;

    public bool IsDue(bool isOpen, double now)
    {
        if (!isOpen)
        {
            _wasOpen = false;

            return false;
        }

        if (!_wasOpen)
        {
            _wasOpen = true;
            _openedAt = now;
            _isDue = true;
        }

        return _isDue && now - _openedAt >= SettleSeconds;
    }

    /// <summary>Check again on the next due tick; the targets changed.</summary>
    public void Request() => _isDue = true;

    /// <summary>The check ran; nothing is due until the bench opens again or a target changes.</summary>
    public void Done() => _isDue = false;
}
