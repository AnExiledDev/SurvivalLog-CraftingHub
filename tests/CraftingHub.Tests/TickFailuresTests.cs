using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>How the ticker answers its own failures: a log that cannot flood, and a stop for a run that keeps failing.</summary>
public class TickFailuresTests
{
    [Fact]
    public void The_first_failure_is_logged_and_later_ones_at_most_once_a_minute_with_how_many_came_between()
    {
        var failures = new TickFailures();
        var run = new FakeRun();

        Assert.Equal("tick failed (1 since the last report, 1 in all): boom", failures.Failed(1_000, "boom", run.Stop));
        Assert.Null(failures.Failed(2_000, "boom", run.Stop));
        Assert.Null(failures.Failed(60_999, "boom", run.Stop));
        Assert.Equal("tick failed (3 since the last report, 4 in all): boom", failures.Failed(61_000, "boom", run.Stop));
    }

    [Fact]
    public void A_run_is_stopped_once_failures_come_twenty_in_a_row_and_not_before()
    {
        var failures = new TickFailures();
        var run = new FakeRun { IsRunning = true };

        Fail(failures, run, TickFailures.BeforeStop - 1);
        Assert.True(run.IsRunning);

        Fail(failures, run, 1);
        Assert.False(run.IsRunning);
    }

    [Fact]
    public void A_run_started_after_failures_while_nothing_ran_is_still_stopped_when_they_carry_on()
    {
        var failures = new TickFailures();
        var run = new FakeRun();

        Fail(failures, run, TickFailures.BeforeStop);
        run.IsRunning = true;
        Fail(failures, run, 1);

        Assert.False(run.IsRunning);
    }

    [Fact]
    public void After_a_stop_the_same_failures_stop_nothing_more_until_a_tick_goes_through()
    {
        var failures = new TickFailures();
        var run = new FakeRun { IsRunning = true };

        Fail(failures, run, TickFailures.BeforeStop);
        run.IsRunning = true;
        Fail(failures, run, 5);

        Assert.True(run.IsRunning);

        failures.Succeeded();
        Fail(failures, run, TickFailures.BeforeStop);

        Assert.False(run.IsRunning);
    }

    private static void Fail(TickFailures failures, FakeRun run, int times)
    {
        for (var i = 0; i < times; i++) failures.Failed(0, "boom", run.Stop);
    }

    /// <summary>The session's stop: it stops a run in progress and says whether there was one.</summary>
    private sealed class FakeRun
    {
        public bool IsRunning { get; set; }

        public bool Stop()
        {
            if (!IsRunning) return false;

            IsRunning = false;

            return true;
        }
    }
}
