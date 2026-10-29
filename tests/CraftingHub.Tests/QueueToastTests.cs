using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>With the workbench closed nobody sees its status line, so the queue's news has to reach the player as a toast.</summary>
public class QueueToastTests
{
    [Fact]
    public void A_queue_that_finishes_with_the_bench_closed_says_so_as_a_done_toast()
    {
        var toast = QueueToast.Of(new RunnerCommand.Stopped("queue finished", IsDone: true), isBenchOpen: false);

        Assert.Equal(("queue finished", QueueToastTone.Done), toast);
    }

    [Fact]
    public void A_queue_that_stops_short_with_the_bench_closed_is_a_warning()
    {
        var toast = QueueToast.Of(new RunnerCommand.Stopped("out of stamina", IsDone: false, IsOutOfStamina: true), isBenchOpen: false);

        Assert.Equal(("out of stamina", QueueToastTone.Warning), toast);
    }

    [Fact]
    public void A_pause_or_a_notice_with_the_bench_closed_is_a_toast()
    {
        Assert.Equal(("carries on when you open it", QueueToastTone.Info), QueueToast.Of(new RunnerCommand.Paused("carries on when you open it"), isBenchOpen: false));
        Assert.Equal(("the bench is busy", QueueToastTone.Info), QueueToast.Of(new RunnerCommand.Notice("the bench is busy"), isBenchOpen: false));
    }

    [Fact]
    public void With_the_bench_open_the_status_line_is_enough()
    {
        Assert.Null(QueueToast.Of(new RunnerCommand.Stopped("queue finished", IsDone: true), isBenchOpen: true));
        Assert.Null(QueueToast.Of(new RunnerCommand.Notice("the bench is busy"), isBenchOpen: true));
    }

    [Fact]
    public void Steps_of_the_run_and_empty_messages_are_not_news()
    {
        Assert.Null(QueueToast.Of(new RunnerCommand.Resumed("queue resumed"), isBenchOpen: false));
        Assert.Null(QueueToast.Of(RunnerCommand.None.Instance, isBenchOpen: false));
        Assert.Null(QueueToast.Of(new RunnerCommand.Notice(""), isBenchOpen: false));
    }
}
