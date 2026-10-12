using System;

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

    /// <summary>
    /// True once per due check, when the bench is free. A check that comes due while the bench is busy is dropped,
    /// not held, since holding it would fire it the moment a run ends.
    /// </summary>
    public bool IsDue(bool isOpen, double now, Func<bool> isIdle)
    {
        if (!IsSettledAndDue(isOpen, now)) return false;

        _isDue = false;

        return isIdle();
    }

    /// <summary>Check again on the next due tick; the targets changed.</summary>
    public void Request() => _isDue = true;

    private bool IsSettledAndDue(bool isOpen, double now)
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
}
