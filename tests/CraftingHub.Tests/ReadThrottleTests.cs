using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>The stamina read: often enough to follow a craft, rarely enough not to ask the game every frame.</summary>
public class ReadThrottleTests
{
    [Fact]
    public void The_first_read_is_due_at_once_whatever_the_clock_says()
    {
        // The clock is the system's milliseconds since boot, so it can be any value, including small ones.
        Assert.True(new ReadThrottle(500).IsDue(0));
        Assert.True(new ReadThrottle(500).IsDue(123_456_789));
        Assert.True(new ReadThrottle(500).IsDue(long.MaxValue));
    }

    [Fact]
    public void After_a_read_the_next_is_due_only_once_the_interval_has_passed()
    {
        var throttle = new ReadThrottle(500);

        Assert.True(throttle.IsDue(10_000));
        Assert.False(throttle.IsDue(10_000));
        Assert.False(throttle.IsDue(10_499));
        Assert.True(throttle.IsDue(10_500));
        Assert.False(throttle.IsDue(10_700));
        Assert.True(throttle.IsDue(11_000));
    }
}
