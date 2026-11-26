using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>A setting that changes what the recipe list counts rebuilds the open window's list with what the game last asked for.</summary>
public class RefreshRepeatTests
{
    [Fact]
    public void Repeating_runs_the_last_rebuild_again_at_the_next_flush()
    {
        var batch = new RefreshBatch<string>();
        var ran = new List<string>();

        batch.RunsNow("open");
        batch.Flush(ran.Add);
        batch.Repeat();

        Assert.True(batch.Flush(ran.Add));
        Assert.Equal(new[] { "open", "open" }, ran);
    }

    [Fact]
    public void After_the_window_closes_repeating_holds_nothing()
    {
        var batch = new RefreshBatch<string>();

        batch.RunsNow("open");
        batch.Flush(_ => { });
        batch.Drop();
        batch.Repeat();

        Assert.False(batch.IsHolding);
    }

    [Fact]
    public void Repeating_before_the_game_asked_for_any_rebuild_holds_nothing()
    {
        var batch = new RefreshBatch<string>();

        batch.Repeat();

        Assert.False(batch.IsHolding);
    }
}
