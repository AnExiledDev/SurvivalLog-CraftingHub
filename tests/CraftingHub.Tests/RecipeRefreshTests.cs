using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>A burst of recipe-list rebuilds runs once, with the newest request, and never while one is already running.</summary>
public class RefreshBatchTests
{
    [Fact]
    public void A_burst_of_rebuild_requests_runs_once_with_the_newest()
    {
        var batch = new RefreshBatch<string>();
        var ran = new List<string>();

        for (var i = 1; i <= 17; i++) Assert.False(batch.RunsNow($"move {i}"));

        Assert.True(batch.Flush(ran.Add));
        Assert.False(batch.Flush(ran.Add));
        Assert.Equal(new[] { "move 17" }, ran);
    }

    [Fact]
    public void A_rebuild_the_game_asks_for_while_the_held_one_runs_goes_straight_through_and_holds_nothing()
    {
        var batch = new RefreshBatch<string>();
        var nested = new List<bool>();

        batch.RunsNow("pull");
        batch.Flush(_ => nested.Add(batch.RunsNow("nested")));

        Assert.Equal(new[] { true }, nested);
        Assert.False(batch.IsHolding);
    }

    [Fact]
    public void Closing_the_window_drops_a_held_rebuild()
    {
        var batch = new RefreshBatch<string>();
        var ran = new List<string>();

        batch.RunsNow("pull");
        batch.Drop();

        Assert.False(batch.Flush(ran.Add));
        Assert.Empty(ran);
    }

    [Fact]
    public void A_rebuild_that_throws_does_not_leave_later_requests_running_straight_through()
    {
        var batch = new RefreshBatch<string>();

        batch.RunsNow("pull");
        Assert.Throws<InvalidOperationException>(() => batch.Flush(_ => throw new InvalidOperationException()));

        Assert.False(batch.RunsNow("next"));
        Assert.True(batch.IsHolding);
    }
}

/// <summary>The craftable test gives the game's answer: largest stacks first, a stack counts whole or not at all.</summary>
public class MaterialPoolTests
{
    private const int Plank = 10;
    private const int Nail = 20;

    [Fact]
    public void Stacks_that_add_up_to_the_need_cover_it()
    {
        var pool = MaterialPool.From(new[] { (Plank, 2), (Plank, 1), (Nail, 4) });

        Assert.True(pool.Covers(Need((Plank, 3), (Nail, 4))));
        Assert.False(pool.Covers(Need((Plank, 4))));
    }

    [Fact]
    public void A_stack_larger_than_what_is_still_needed_is_passed_over_as_the_game_does()
    {
        // Five planks in one stack do not cover a need of three: the game never splits a stack here.
        Assert.False(MaterialPool.From(new[] { (Plank, 5) }).Covers(Need((Plank, 3))));

        // Largest first: 5 is skipped, then 2 and 1 make three.
        Assert.True(MaterialPool.From(new[] { (Plank, 1), (Plank, 5), (Plank, 2) }).Covers(Need((Plank, 3))));

        // 3 is taken first, leaving one that the remaining 2 cannot fill.
        Assert.False(MaterialPool.From(new[] { (Plank, 2), (Plank, 3) }).Covers(Need((Plank, 4))));
    }

    [Fact]
    public void Which_stacks_count_depends_on_trying_the_largest_first()
    {
        var stacks = new[] { (Plank, 2), (Plank, 2), (Plank, 3) };

        // 3 then 2 make five; smallest first would stop at 2 + 2 with the 3 too big.
        Assert.True(MaterialPool.From(stacks).Covers(Need((Plank, 5))));

        // 3 leaves one that neither 2 fits; smallest first would have made four from 2 + 2.
        Assert.False(MaterialPool.From(stacks).Covers(Need((Plank, 4))));
    }

    [Fact]
    public void A_stack_of_zero_or_less_counts_as_one()
    {
        var pool = MaterialPool.From(new[] { (Plank, 0), (Plank, -3) });

        Assert.True(pool.Covers(Need((Plank, 2))));
        Assert.False(pool.Covers(Need((Plank, 3))));
    }

    [Fact]
    public void A_material_with_no_stack_fails_even_when_none_of_it_is_needed()
    {
        var pool = MaterialPool.From(new[] { (Plank, 2) });

        Assert.False(pool.Covers(Need((Nail, 0))));
        Assert.True(pool.Covers(Need((Plank, 0))));
    }

    [Fact]
    public void Empty_storages_cover_nothing_and_a_recipe_needing_nothing_is_covered_by_any_stock()
    {
        Assert.False(MaterialPool.From(Array.Empty<(int, int)>()).Covers(Need()));
        Assert.True(MaterialPool.From(new[] { (Plank, 1) }).Covers(Need()));
    }

    [Fact]
    public void A_storage_listed_twice_counts_twice_as_the_game_does()
    {
        var once = new[] { (Plank, 2) };

        Assert.False(MaterialPool.From(once).Covers(Need((Plank, 4))));
        Assert.True(MaterialPool.From(once.Concat(once)).Covers(Need((Plank, 4))));
    }

    private static Dictionary<int, int> Need(params (int ItemId, int Count)[] materials) =>
        materials.ToDictionary(material => material.ItemId, material => material.Count);
}
