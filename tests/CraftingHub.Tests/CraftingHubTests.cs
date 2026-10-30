using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>A small woodworking bench: logs make planks, fibre makes rope, planks and rope make a crate.</summary>
internal static class Bench
{
    public const int Log = 1;
    public const int Plank = 2;
    public const int Fibre = 3;
    public const int Rope = 4;
    public const int Crate = 5;
    public const int Nail = 6;

    public static readonly Recipe PlankRecipe = new("plank", "Plank", Plank, Yield: 2, Mats((Log, 1)), IsLocked: false);
    public static readonly Recipe RopeRecipe = new("rope", "Rope", Rope, Yield: 1, Mats((Fibre, 3)), IsLocked: false);
    public static readonly Recipe CrateRecipe = new("crate", "Crate", Crate, Yield: 1, Mats((Plank, 4), (Rope, 1)), IsLocked: false);

    public static RecipeBook Book(params Recipe[] extra) => new(new[] { PlankRecipe, RopeRecipe, CrateRecipe }.Concat(extra));

    public static Dictionary<int, int> Mats(params (int Id, int Count)[] pairs) => pairs.ToDictionary(pair => pair.Id, pair => pair.Count);

    public static string Name(int id) => id switch
    {
        Log => "Log",
        Plank => "Plank",
        Fibre => "Fibre",
        Rope => "Rope",
        Nail => "Nail",
        _ => "Item " + id,
    };
}

public class CraftPlannerTests
{
    [Fact]
    public void Owned_materials_craft_the_target_directly()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        var ready = Assert.IsType<CraftPlan.Ready>(plan);
        Assert.Equal(new[] { ("crate", 1) }, Steps(ready));
    }

    [Fact]
    public void Missing_intermediates_are_made_first_from_raw_materials()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), Bench.CrateRecipe, 1, maxDepth: 4);

        var ready = Assert.IsType<CraftPlan.Ready>(plan);
        Assert.Equal(new[] { ("plank", 2), ("rope", 1), ("crate", 1) }, Steps(ready));
    }

    [Fact]
    public void Owned_intermediates_are_used_before_crafting_more()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Plank, 3), (Bench.Log, 5), (Bench.Rope, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        var ready = Assert.IsType<CraftPlan.Ready>(plan);
        Assert.Equal(new[] { ("plank", 1), ("crate", 1) }, Steps(ready));
    }

    [Fact]
    public void Yield_rounds_crafts_up_so_odd_needs_are_covered()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Log, 3), (Bench.Rope, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Equal(new[] { ("plank", 2), ("crate", 1) }, Steps(Assert.IsType<CraftPlan.Ready>(plan)));
    }

    [Fact]
    public void Several_crafts_reserve_stock_so_planks_are_not_counted_twice()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Plank, 4), (Bench.Log, 2), (Bench.Rope, 2)), Bench.CrateRecipe, 2, maxDepth: 4);

        Assert.Equal(new[] { ("plank", 2), ("crate", 2) }, Steps(Assert.IsType<CraftPlan.Ready>(plan)));
    }

    [Fact]
    public void Two_intermediates_drawing_on_the_same_raw_material_both_count_against_it()
    {
        var ropeFromLog = Bench.RopeRecipe with { Materials = Bench.Mats((Bench.Log, 1)) };
        var book = new RecipeBook(new[] { Bench.PlankRecipe, ropeFromLog, Bench.CrateRecipe });

        var plan = CraftPlanner.Plan(book, Bench.Mats((Bench.Log, 2)), Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Equal(1, Assert.IsType<CraftPlan.Short>(plan).Missing[Bench.Log]);
    }

    [Fact]
    public void Too_few_raw_materials_reports_exactly_what_is_short()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Log, 1), (Bench.Fibre, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        var shortOf = Assert.IsType<CraftPlan.Short>(plan);
        Assert.Equal(1, shortOf.Missing[Bench.Log]);
        Assert.Equal(2, shortOf.Missing[Bench.Fibre]);
    }

    [Fact]
    public void Depth_zero_turns_intermediates_off()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), Bench.CrateRecipe, 1, maxDepth: 0);

        var shortOf = Assert.IsType<CraftPlan.Short>(plan);
        Assert.Equal(4, shortOf.Missing[Bench.Plank]);
    }

    [Fact]
    public void A_recipe_that_needs_its_own_product_is_short_instead_of_looping()
    {
        var nailFromCrate = new Recipe("nail", "Nail", Bench.Nail, 1, Bench.Mats((Bench.Crate, 1)), false);
        var crateFromNail = new Recipe("crate2", "Crate", Bench.Crate, 1, Bench.Mats((Bench.Nail, 1)), false);
        var book = new RecipeBook(new[] { nailFromCrate, crateFromNail });

        var plan = CraftPlanner.Plan(book, new Dictionary<int, int>(), crateFromNail, 1, maxDepth: 10);

        Assert.IsType<CraftPlan.Short>(plan);
    }

    [Fact]
    public void Locked_recipes_are_never_planned_as_intermediates()
    {
        var lockedPlank = Bench.PlankRecipe with { IsLocked = true };
        var book = new RecipeBook(new[] { lockedPlank, Bench.RopeRecipe, Bench.CrateRecipe });

        var plan = CraftPlanner.Plan(book, Bench.Mats((Bench.Log, 2), (Bench.Rope, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.IsType<CraftPlan.Short>(plan);
    }

    [Fact]
    public void The_callers_stock_is_left_untouched()
    {
        var stock = Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3));

        CraftPlanner.Plan(Bench.Book(), stock, Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Equal(2, stock[Bench.Log]);
        Assert.False(stock.ContainsKey(Bench.Plank));
    }

    [Fact]
    public void A_walk_hands_back_what_the_plan_leaves_in_stock()
    {
        var walk = CraftPlanner.Walk(Bench.Book(), Bench.Mats((Bench.Log, 3), (Bench.Fibre, 3)), Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Empty(walk.Missing);
        Assert.Equal((1, 0, 0, 1), (walk.Left[Bench.Log], walk.Left[Bench.Fibre], walk.Left[Bench.Plank], walk.Left[Bench.Crate]));
    }

    [Fact]
    public void A_walk_goes_on_through_a_shortage_and_counts_every_missing_item()
    {
        var walk = CraftPlanner.Walk(Bench.Book(), Bench.Mats((Bench.Log, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Equal(new[] { ("plank", 2), ("rope", 1), ("crate", 1) }, walk.Steps.Select(step => (step.Recipe.Key, step.Crafts)));
        Assert.Equal(new[] { (Bench.Log, 1), (Bench.Fibre, 3) }, walk.Missing.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        Assert.Equal(0, walk.Left[Bench.Log]);
    }

    [Fact]
    public void A_walk_leaves_the_callers_stock_alone_and_carries_unused_items_into_what_is_left()
    {
        var stock = Bench.Mats((Bench.Log, 3), (Bench.Fibre, 3), (Bench.Nail, 7));

        var walk = CraftPlanner.Walk(Bench.Book(), stock, Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Equal((7, 1, 1), (walk.Left[Bench.Nail], walk.Left[Bench.Log], walk.Left[Bench.Crate]));
        Assert.Equal(new[] { (Bench.Log, 3), (Bench.Fibre, 3), (Bench.Nail, 7) }, stock.Select(pair => (pair.Key, pair.Value)));
    }

    [Fact]
    public void A_count_the_plan_changed_is_read_back_from_the_plan_not_the_stock()
    {
        // The planks are made and then used up within the one plan, so how many there are after making them has to
        // come from the plan itself: the stock it was given still says none.
        var walk = CraftPlanner.Walk(Bench.Book(), Bench.Mats((Bench.Log, 23), (Bench.Rope, 10)), Bench.CrateRecipe, 10, maxDepth: 4);

        Assert.Equal(new[] { ("plank", 20), ("crate", 10) }, walk.Steps.Select(step => (step.Recipe.Key, step.Crafts)));
        Assert.Equal((3, 0, 0, 10), (walk.Left[Bench.Log], walk.Left[Bench.Plank], walk.Left[Bench.Rope], walk.Left[Bench.Crate]));
    }

    private static (string, int)[] Steps(CraftPlan.Ready ready) => ready.Steps.Select(step => (step.Recipe.Key, step.Crafts)).ToArray();
}

public class ChainMarksTests
{
    [Fact]
    public void A_recipe_craftable_only_through_intermediates_is_marked_with_what_it_makes_first()
    {
        var marks = ChainMarks.Find(Bench.Book(), Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), new HashSet<string>(), maxDepth: 4);

        Assert.Equal("Makes first: Plank ×2, Rope ×1", marks["crate"]);
    }

    [Fact]
    public void Recipes_the_game_already_allows_are_not_marked()
    {
        var marks = ChainMarks.Find(Bench.Book(), Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1), (Bench.Log, 9)), new HashSet<string> { "crate", "plank" }, maxDepth: 4);

        Assert.Empty(marks);
    }

    [Fact]
    public void A_recipe_needing_no_intermediates_is_not_marked_even_if_the_game_disagrees()
    {
        var marks = ChainMarks.Find(Bench.Book(), Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), new HashSet<string>(), maxDepth: 4);

        Assert.False(marks.ContainsKey("crate"));
    }

    [Fact]
    public void Recipes_still_short_after_intermediates_are_not_marked()
    {
        var marks = ChainMarks.Find(Bench.Book(), Bench.Mats((Bench.Log, 1)), new HashSet<string>(), maxDepth: 4);

        Assert.False(marks.ContainsKey("crate"));
    }

    [Fact]
    public void Depth_zero_marks_nothing()
    {
        Assert.Empty(ChainMarks.Find(Bench.Book(), Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), new HashSet<string>(), maxDepth: 0));
    }
}

public class HomeOwnersTests
{
    [Fact]
    public void Only_home_storages_not_already_linked_are_added()
    {
        var added = HomeOwners.ToAdd(new long[] { 10, 11 }, new long[] { 11, 12, 13, 12 }, excluded: new long[] { 13 });

        Assert.Equal(new long[] { 12 }, added);
    }

    [Fact]
    public void No_owner_is_never_added()
    {
        Assert.Empty(HomeOwners.ToAdd(new long[0], new long[] { 0 }, new long[0]));
    }

    [Fact]
    public void Reused_buffers_left_over_from_the_last_ask_do_not_change_the_answer()
    {
        var seen = new HashSet<long> { 12 };
        var added = new List<long> { 99 };

        var result = HomeOwners.ToAdd(new long[] { 10, 11 }, new long[] { 11, 12, 13, 12 }, new long[] { 13 }, seen, added);

        Assert.Equal(new long[] { 12 }, result);
        Assert.Same(added, result);
    }
}

public class CraftQueueTests
{
    [Fact]
    public void Adding_the_same_recipe_twice_in_a_row_tops_up_one_order()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 2);
        queue.Add("crate", "Crate", 3);

        Assert.Equal(5, Assert.Single(queue.Entries).Crafts);
    }

    [Fact]
    public void Craft_now_jumps_ahead_of_waiting_orders()
    {
        var queue = new CraftQueue();

        queue.Add("rope", "Rope", 1);
        queue.PushFront("crate", "Crate", 1);

        Assert.Equal("crate", queue.Head?.Key);
    }

    [Fact]
    public void Progress_counts_down_the_head_and_drops_it_when_finished()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 3);
        queue.Add("rope", "Rope", 1);

        var crate = queue.Entries[0].Id;

        queue.Progress(crate, 2);

        Assert.Equal(1, queue.Head?.Crafts);

        queue.Progress(crate, 5);

        Assert.Equal("rope", queue.Head?.Key);
    }

    [Fact]
    public void Craft_now_during_a_step_goes_behind_the_order_being_made()
    {
        var queue = new CraftQueue();

        queue.Add("plank", "Plank", 10);
        queue.Add("rope", "Rope", 1);
        queue.PushFront("crate", "Crate", 1, behindHead: true);

        Assert.Equal(new[] { "plank", "crate", "rope" }, queue.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void Finished_crafts_go_to_the_order_they_were_planned_for_even_when_another_jumped_ahead()
    {
        var queue = new CraftQueue();

        queue.Add("plank", "Plank", 10);

        var plank = queue.Entries[0].Id;

        queue.PushFront("crate", "Crate", 1);
        queue.Progress(plank, 10);

        Assert.Equal(new[] { ("crate", 1) }, queue.Entries.Select(entry => (entry.Key, entry.Crafts)));
    }

    [Fact]
    public void Crafts_for_an_order_no_longer_queued_are_credited_to_no_other()
    {
        var queue = new CraftQueue();

        queue.Add("plank", "Plank", 2);

        var plank = queue.Entries[0].Id;

        queue.Remove(plank);
        queue.Add("rope", "Rope", 1);
        queue.Progress(plank, 2);

        Assert.Equal(1, Assert.Single(queue.Entries).Crafts);
    }

    [Fact]
    public void Removing_an_order_that_is_not_queued_changes_nothing()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 1);

        var crate = queue.Entries[0].Id;

        queue.Remove(crate + 100);
        queue.Remove(0);

        Assert.Single(queue.Entries);
    }

    [Fact]
    public void Removing_takes_off_the_order_asked_for_wherever_it_has_moved_to()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 1);
        queue.Add("rope", "Rope", 1);

        var crate = queue.Entries[0].Id;
        var rope = queue.Entries[1].Id;

        queue.Move(rope, 0);
        queue.Remove(crate);

        Assert.Equal(new[] { "rope" }, queue.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void A_top_up_is_never_merged_into_the_players_order_for_the_same_recipe()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 1);
        queue.Add("crate", "Crate", 2, isTopUp: true);

        Assert.Equal(new[] { (1, false), (2, true) }, queue.Entries.Select(entry => (entry.Crafts, entry.IsTopUp)));
    }

    [Fact]
    public void Moving_an_order_puts_it_at_the_place_asked_and_past_either_end_at_that_end()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 1);
        queue.Add("rope", "Rope", 1);
        queue.Add("plank", "Plank", 1);

        var crate = queue.Entries[0].Id;
        var plank = queue.Entries[2].Id;

        queue.Move(plank, 0);

        Assert.Equal(new[] { "plank", "crate", "rope" }, queue.Entries.Select(entry => entry.Key));

        queue.Move(crate, 99);

        Assert.Equal(new[] { "plank", "rope", "crate" }, queue.Entries.Select(entry => entry.Key));

        queue.Move(crate, -5);

        Assert.Equal(new[] { "crate", "plank", "rope" }, queue.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void Moving_an_order_no_longer_queued_changes_nothing()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 1);
        queue.Add("rope", "Rope", 1);

        var crate = queue.Entries[0].Id;

        queue.Remove(crate);
        queue.Add("plank", "Plank", 1);
        queue.Move(crate, 1);

        Assert.Equal(new[] { "rope", "plank" }, queue.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void Setting_an_amount_keeps_the_order_so_finished_crafts_still_count_against_it()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 2);

        var crate = queue.Entries[0].Id;

        queue.SetCrafts(crate, 5);
        queue.Progress(crate, 1);

        Assert.Equal(4, Assert.Single(queue.Entries).Crafts);
    }

    [Fact]
    public void An_amount_of_zero_or_for_an_unknown_order_changes_nothing()
    {
        var queue = new CraftQueue();

        queue.Add("crate", "Crate", 2);

        var crate = queue.Entries[0].Id;

        queue.SetCrafts(crate, 0);
        queue.SetCrafts(crate, -3);
        queue.SetCrafts(crate + 100, 7);

        Assert.Equal(2, Assert.Single(queue.Entries).Crafts);
    }
}

public class QueueForecastTests
{
    private static readonly KeepTarget[] NoKeeps = Array.Empty<KeepTarget>();

    [Fact]
    public void Each_order_says_whether_it_is_ready_makes_parts_first_or_is_short()
    {
        var ready = Plan(Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), NoKeeps, Order("crate", 1));
        var makesFirst = Plan(Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), NoKeeps, Order("crate", 1));
        var shortOf = Plan(Bench.Mats((Bench.Log, 2)), NoKeeps, Order("crate", 1));

        Assert.Equal((OrderState.Ready, "Ready"), Outlook(ready, 0));
        Assert.Equal((OrderState.MakesFirst, "Makes first: Plank x2, Rope x1"), Outlook(makesFirst, 0));
        Assert.Equal((OrderState.Short, "Short: Fibre x3"), Outlook(shortOf, 0));
    }

    [Fact]
    public void A_locked_or_unlisted_recipe_says_so()
    {
        var book = new RecipeBook(new[] { Bench.PlankRecipe, Bench.RopeRecipe with { IsLocked = true } });

        var outlook = QueueForecast.Plan(new[] { Order("rope", 1, id: 1), Order("anvil", 1, id: 2) }, book, Bench.Mats((Bench.Fibre, 9)), NoKeeps, 4, Bench.Name);

        Assert.Equal((OrderState.Locked, "Not unlocked yet"), Outlook(outlook, 0));
        Assert.Equal((OrderState.NotListed, "Not in your recipe list yet"), Outlook(outlook, 1));
    }

    [Fact]
    public void A_later_order_only_gets_what_the_orders_above_it_leave()
    {
        var outlook = Plan(Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), NoKeeps, Order("crate", 1, id: 1), Order("crate", 1, id: 2));

        Assert.Equal((OrderState.Ready, "Ready"), Outlook(outlook, 0));
        Assert.Equal((OrderState.Short, "Short: Log x2, Fibre x3"), Outlook(outlook, 1));
    }

    [Fact]
    public void Totals_are_what_the_queue_uses_from_what_you_own_short_items_first()
    {
        var outlook = Plan(Bench.Mats((Bench.Log, 2), (Bench.Fibre, 1)), NoKeeps, Order("crate", 1));

        Assert.Equal(new[] { new MaterialTotal(Bench.Fibre, "Fibre", 3, 1, 2), new MaterialTotal(Bench.Log, "Log", 2, 2, 0) }, outlook.Totals);
    }

    [Fact]
    public void The_heads_up_names_the_first_order_the_queue_will_stop_at()
    {
        var outlook = Plan(Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), NoKeeps, Order("crate", 1, id: 1), Order("rope", 1, id: 2), Order("plank", 1, id: 3));

        Assert.Equal("Heads up: Rope is short of Fibre x3. The queue will stop there.", outlook.HeadsUp);
    }

    [Fact]
    public void A_queue_that_can_all_be_made_has_no_heads_up()
    {
        var outlook = Plan(Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), NoKeeps, Order("crate", 1));

        Assert.Equal("", outlook.HeadsUp);
    }

    [Fact]
    public void A_top_up_leaves_kept_stock_alone_where_a_players_order_may_use_it()
    {
        var stock = Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1), (Bench.Log, 2));
        var kept = new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 4) };

        var players = Plan(stock, kept, Order("crate", 1));
        var topUp = Plan(stock, kept, Order("crate", 1) with { IsTopUp = true });

        Assert.Equal((OrderState.Ready, "Ready"), Outlook(players, 0));
        Assert.Equal((OrderState.MakesFirst, "Makes first: Plank x2"), Outlook(topUp, 0));
    }

    /// <summary>An order named as its recipe: "crate" is Crate.</summary>
    private static QueueEntry Order(string key, int crafts, long id = 1) => new(key, char.ToUpperInvariant(key[0]) + key.Substring(1), crafts, Id: id);

    private static QueueOutlook Plan(Dictionary<int, int> stock, KeepTarget[] kept, params QueueEntry[] queue) =>
        QueueForecast.Plan(queue, Bench.Book(), stock, kept, 4, Bench.Name);

    private static (OrderState, string) Outlook(QueueOutlook outlook, int index) => (outlook.Orders[index].State, outlook.Orders[index].Note);
}

public class QueuePlannerTests
{
    [Fact]
    public void An_empty_queue_is_finished()
    {
        Assert.IsType<NextStep.Finished>(QueuePlanner.Next(null, Bench.Book(), new Dictionary<int, int>(), NoKeeps, 4, Bench.Name));
    }

    [Fact]
    public void The_first_step_is_the_first_intermediate_and_is_not_the_target()
    {
        var next = QueuePlanner.Next(new QueueEntry("crate", "Crate", 1), Bench.Book(), Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), NoKeeps, 4, Bench.Name);

        var craft = Assert.IsType<NextStep.Craft>(next);
        Assert.Equal("plank", craft.Step.Recipe.Key);
        Assert.False(craft.IsTarget);
    }

    [Fact]
    public void Short_stock_blocks_with_the_missing_items_named()
    {
        var next = QueuePlanner.Next(new QueueEntry("crate", "Crate", 1), Bench.Book(), Bench.Mats((Bench.Log, 2)), NoKeeps, 4, Bench.Name);

        Assert.Equal("can't make Crate ×1: short of Fibre ×3", Assert.IsType<NextStep.Blocked>(next).Reason);
    }

    [Fact]
    public void A_recipe_missing_from_the_recipe_list_blocks_without_sending_the_player_to_another_workbench()
    {
        var next = QueuePlanner.Next(new QueueEntry("anvil", "Anvil", 1), Bench.Book(), new Dictionary<int, int>(), NoKeeps, 4, Bench.Name);

        Assert.Equal("Anvil is not in your recipe list yet", Assert.IsType<NextStep.Blocked>(next).Reason);
    }

    [Fact]
    public void A_players_order_may_use_kept_stock_but_a_top_up_makes_its_own()
    {
        var stock = Bench.Mats((Bench.Plank, 4), (Bench.Log, 2), (Bench.Rope, 1));
        var kept = new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 4) };

        var players = QueuePlanner.Next(new QueueEntry("crate", "Crate", 1), Bench.Book(), stock, kept, 4, Bench.Name);
        var topUp = QueuePlanner.Next(new QueueEntry("crate", "Crate", 1, IsTopUp: true), Bench.Book(), stock, kept, 4, Bench.Name);

        Assert.Equal("crate", Assert.IsType<NextStep.Craft>(players).Step.Recipe.Key);
        Assert.Equal("plank", Assert.IsType<NextStep.Craft>(topUp).Step.Recipe.Key);
    }

    private static readonly KeepTarget[] NoKeeps = Array.Empty<KeepTarget>();
}

public class QueueRunnerTests
{
    private static readonly CraftStep PlankStep = new(Bench.PlankRecipe, 3);
    private static readonly IReadOnlyDictionary<int, int> Empty = new Dictionary<int, int>();
    private static readonly IReadOnlyDictionary<int, int> LogsLaidOut = Bench.Mats((Bench.Log, 1));

    private const long RunBench = 1;
    private const long OtherBench = 2;
    private static readonly BenchView Closed = new(false, false, false, 0, Empty, 0);

    private static BenchView Idle(IReadOnlyDictionary<int, int>? contents = null, int max = 0, long bench = RunBench) => new(true, false, false, max, contents ?? Empty, 0, bench);

    private static NextStep PlankNext() => new NextStep.Craft(PlankStep, IsTarget: true);

    [Fact]
    public void A_full_step_is_click_recipe_then_craft_then_confirm_then_done()
    {
        var runner = new QueueRunner();

        runner.Start();

        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), 0, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(LogsLaidOut, 3), 0.1, PlankNext));
        Assert.Equal(new RunnerCommand.Craft(3), runner.Tick(Idle(LogsLaidOut, 3), 0.4, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(new BenchView(true, true, false, 3, LogsLaidOut, 1), 1, PlankNext));
        Assert.IsType<RunnerCommand.ConfirmResult>(runner.Tick(new BenchView(true, false, true, 0, Empty, 3), 2, PlankNext));

        var done = Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Idle(), 2.1, PlankNext));
        Assert.Equal(3, done.Crafts);
        Assert.True(done.IsTarget);
    }

    [Fact]
    public void A_batch_is_never_larger_than_the_game_allows()
    {
        var runner = new QueueRunner();

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);
        runner.Tick(Idle(LogsLaidOut, 2), 0.1, PlankNext);

        Assert.Equal(new RunnerCommand.Craft(2), runner.Tick(Idle(LogsLaidOut, 2), 0.5, PlankNext));
    }

    [Fact]
    public void The_result_is_not_confirmed_while_a_batch_is_still_running()
    {
        var runner = CraftingRunner();

        Assert.IsType<RunnerCommand.None>(runner.Tick(new BenchView(true, true, true, 0, Empty, 1), 1, PlankNext));
    }

    [Fact]
    public void A_short_batch_reports_the_crafts_the_game_finished()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 2, RunBench, BatchActive: true, IsCrafting: true), 1, PlankNext);
        runner.Tick(new BenchView(true, false, true, 0, Empty, 2), 2, PlankNext);

        Assert.Equal(2, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Idle(), 3, PlankNext)).Crafts);
    }

    [Fact]
    public void A_batch_count_left_over_from_an_earlier_batch_is_not_taken_as_this_steps()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, false, false, 0, Empty, 5), 0.6, PlankNext);
        runner.Tick(new BenchView(true, true, false, 0, Empty, 5, RunBench, BatchActive: true), 0.75, PlankNext);
        runner.Tick(new BenchView(true, true, false, 0, Empty, 0, RunBench, BatchActive: true, IsCrafting: true), 1, PlankNext);
        runner.Tick(new BenchView(true, true, false, 0, Empty, 1, RunBench, BatchActive: true, IsCrafting: true), 2, PlankNext);

        Assert.Equal(1, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Closed, 2.1, PlankNext)).Crafts);
    }

    [Fact]
    public void The_last_craft_of_a_batch_counts_though_the_game_ends_the_batch_as_it_finishes()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 1, RunBench, BatchActive: true, IsCrafting: true), 1, PlankNext);
        runner.Tick(new BenchView(true, false, true, 0, Empty, 2), 2, PlankNext);

        Assert.Equal(2, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Idle(), 3, PlankNext)).Crafts);
    }

    [Fact]
    public void A_finished_craft_with_no_result_screen_still_completes()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 0), 1, PlankNext);
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 1.5, PlankNext));

        Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Idle(), 3.1, PlankNext));
    }

    [Fact]
    public void Materials_that_never_appear_stop_the_run_with_a_reason()
    {
        var runner = new QueueRunner();

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), QueueRunner.FillTimeoutSeconds + 0.1, PlankNext));
        Assert.Contains("Plank", stopped.Reason);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void A_recipe_click_the_game_dropped_is_sent_again_and_the_step_then_crafts()
    {
        var runner = new QueueRunner();
        var retry = QueueRunner.RecipeClickRetrySeconds;

        runner.Start();

        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), 0, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), retry / 2, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), retry, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), retry * 1.5, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), retry * 2, PlankNext));

        // The game can hold off a click for a few seconds after a craft; the run waits that out.
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(LogsLaidOut, 3), 3.5, PlankNext));
        Assert.Equal(new RunnerCommand.Craft(3), runner.Tick(Idle(LogsLaidOut, 3), 3.75, PlankNext));
    }

    [Fact]
    public void A_bench_holding_the_recipes_materials_is_not_clicked_again_while_the_game_recounts()
    {
        var runner = new QueueRunner();
        var retry = QueueRunner.RecipeClickRetrySeconds;

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(LogsLaidOut, 0), retry, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(LogsLaidOut, 0), retry * 2, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(LogsLaidOut, 3), 2, PlankNext));
        Assert.Equal(new RunnerCommand.Craft(3), runner.Tick(Idle(LogsLaidOut, 3), 2 + QueueRunner.SettleSeconds, PlankNext));
    }

    [Fact]
    public void A_busy_bench_is_not_clicked_again()
    {
        var runner = new QueueRunner();
        var retry = QueueRunner.RecipeClickRetrySeconds;
        var busy = new BenchView(true, true, false, 0, Empty, 0, RunBench);

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        Assert.IsType<RunnerCommand.None>(runner.Tick(busy, retry, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(busy, retry * 2, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), retry * 3, PlankNext));
    }

    [Fact]
    public void A_bench_that_never_fills_is_clicked_again_until_the_run_stops_with_a_reason()
    {
        var runner = new QueueRunner();
        var retry = QueueRunner.RecipeClickRetrySeconds;

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        for (var now = retry; now <= QueueRunner.FillTimeoutSeconds; now += retry) Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), now, PlankNext));

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), QueueRunner.FillTimeoutSeconds + 0.1, PlankNext));
        Assert.Equal("the game did not lay out the materials for Plank", stopped.Reason);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void A_laid_out_bench_the_game_allows_no_craft_from_stops_the_run_saying_so_rather_than_blaming_the_layout()
    {
        var runner = new QueueRunner();

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);
        runner.Tick(Idle(LogsLaidOut, 0), 1, PlankNext);

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(LogsLaidOut, 0), QueueRunner.FillTimeoutSeconds + 0.1, PlankNext));
        Assert.Equal("the materials for Plank are laid out but the game allows no craft of it", stopped.Reason);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void Wrong_materials_on_the_bench_do_not_start_a_craft()
    {
        var runner = new QueueRunner();

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);
        runner.Tick(Idle(Bench.Mats((Bench.Log, 3), (Bench.Fibre, 1)), 3), 0.1, PlankNext);

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(Bench.Mats((Bench.Log, 3), (Bench.Fibre, 1)), 3), 0.5, PlankNext));
    }

    [Fact]
    public void A_craft_that_never_starts_stops_the_run()
    {
        var runner = CraftingRunner();

        Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), QueueRunner.StartTimeoutSeconds + 1, PlankNext));
    }

    [Fact]
    public void With_resuming_off_closing_the_workbench_stops_the_run()
    {
        var runner = CraftingRunner(resumesOnReopen: false);

        Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Closed, 1, PlankNext));
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void Resuming_turned_off_during_a_run_makes_the_next_close_stop_it()
    {
        var runner = CraftingRunner();

        runner.Use(resumesOnReopen: false, resumesAtOtherBenches: false);

        Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Closed, 1, PlankNext));
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void Other_workbenches_turned_on_while_paused_let_the_run_carry_on_at_another()
    {
        var runner = PausedRunner();

        runner.Use(resumesOnReopen: true, resumesAtOtherBenches: true);

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(bench: OtherBench), 10, PlankNext));
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(bench: OtherBench), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));
    }

    [Fact]
    public void Closing_the_workbench_pauses_the_run_instead_of_ending_it()
    {
        var runner = CraftingRunner();

        Assert.IsType<RunnerCommand.Paused>(runner.Tick(Closed, 1, PlankNext));
        Assert.True(runner.IsRunning);
        Assert.True(runner.IsPaused);
    }

    [Fact]
    public void A_paused_run_carries_on_once_its_workbench_has_been_open_a_moment()
    {
        var runner = PausedRunner();

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 10, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 10.5, PlankNext));
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), 11.1, PlankNext));
    }

    [Fact]
    public void A_workbench_closed_again_before_the_run_carries_on_restarts_the_wait()
    {
        var runner = PausedRunner();

        runner.Tick(Idle(), 10, PlankNext);
        runner.Tick(Closed, 10.25, PlankNext);
        runner.Tick(Idle(), 10.5, PlankNext);

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 11.25, PlankNext));
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(), 10.5 + QueueRunner.ResumeSettleSeconds, PlankNext));
    }

    [Fact]
    public void A_paused_run_waits_for_its_own_workbench_and_says_so_once()
    {
        var runner = PausedRunner();

        Assert.IsType<RunnerCommand.Paused>(runner.Tick(Idle(bench: OtherBench), 10, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(bench: OtherBench), 10.1, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(bench: OtherBench), 30, PlankNext));
        Assert.True(runner.IsPaused);
    }

    [Fact]
    public void Start_at_another_workbench_leaves_a_paused_run_waiting_for_its_own()
    {
        var runner = PausedRunner();

        runner.Tick(Idle(bench: OtherBench), 10, PlankNext);
        runner.Start();

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(bench: OtherBench), 10.1, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(bench: OtherBench), 10 + QueueRunner.ResumeSettleSeconds + 1, PlankNext));
        Assert.True(runner.IsPaused);
    }

    [Fact]
    public void Stop_then_Start_runs_the_queue_at_the_workbench_open_now()
    {
        var runner = PausedRunner();

        runner.Tick(Idle(bench: OtherBench), 10, PlankNext);
        runner.Stop("stopped");
        runner.Start();

        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(bench: OtherBench), 10.1, PlankNext));
    }

    [Fact]
    public void With_other_workbenches_on_a_paused_run_carries_on_at_whichever_workbench_opens()
    {
        var runner = PausedRunner(resumesAtOtherBenches: true);

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(bench: OtherBench), 10, PlankNext));
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(bench: OtherBench), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(bench: OtherBench), 11.1, PlankNext));
    }

    [Fact]
    public void Carrying_on_at_its_own_workbench_says_that_workbench_is_open_again()
    {
        var runner = PausedRunner();

        runner.Tick(Idle(), 10, PlankNext);

        var resumed = Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));
        Assert.Contains("open again", resumed.Reason);
    }

    [Fact]
    public void With_other_workbenches_on_carrying_on_does_not_claim_the_same_workbench_reopened()
    {
        var runner = PausedRunner(resumesAtOtherBenches: true);

        runner.Tick(Idle(bench: OtherBench), 10, PlankNext);

        var resumed = Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(bench: OtherBench), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));
        Assert.DoesNotContain("again", resumed.Reason);
        Assert.Contains("a workbench is open", resumed.Reason);
    }

    [Fact]
    public void With_other_workbenches_on_closing_tells_the_player_any_workbench_will_do()
    {
        var runner = CraftingRunner(resumesAtOtherBenches: true);

        var paused = Assert.IsType<RunnerCommand.Paused>(runner.Tick(Closed, 1, PlankNext));
        Assert.Contains("any workbench", paused.Reason);
    }

    [Fact]
    public void With_other_workbenches_on_crafts_finished_before_the_close_count_once_and_the_run_moves_on()
    {
        var runner = CraftingRunner(resumesAtOtherBenches: true);

        runner.Tick(new BenchView(true, true, false, 0, Empty, 2, RunBench, BatchActive: true, BatchSuccess: 2, IsCrafting: true), 1, PlankNext);
        Assert.Equal(2, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Closed, 1.1, PlankNext)).Crafts);
        Assert.IsType<RunnerCommand.Paused>(runner.Tick(Closed, 1.2, PlankNext));

        runner.Tick(Idle(bench: OtherBench), 10, PlankNext);
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(bench: OtherBench), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(bench: OtherBench), 11.1, PlankNext));
    }

    [Fact]
    public void With_other_workbenches_on_craft_now_is_still_refused_while_paused()
    {
        var runner = PausedRunner(resumesAtOtherBenches: true);

        Assert.Equal(CraftNowPlace.Refused, runner.CraftNowPlace);

        runner.Tick(Idle(bench: OtherBench), 10, PlankNext);

        Assert.Equal(CraftNowPlace.Refused, runner.CraftNowPlace);
    }

    [Fact]
    public void Stop_ends_a_paused_run()
    {
        var runner = PausedRunner();

        runner.Stop("stopped");

        Assert.False(runner.IsRunning);
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 20, PlankNext));
    }

    [Fact]
    public void Crafts_the_batch_finished_before_the_close_count_against_the_order()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 2, RunBench, BatchActive: true, BatchSuccess: 2, IsCrafting: true), 1, PlankNext);

        var done = Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Closed, 1.1, PlankNext));
        Assert.Equal(2, done.Crafts);
        Assert.IsType<RunnerCommand.Paused>(runner.Tick(Closed, 1.2, PlankNext));
    }

    [Fact]
    public void A_craft_that_finished_just_before_the_close_counts()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 1, RunBench, BatchActive: true, BatchSuccess: 1, IsCrafting: true), 1, PlankNext);

        var closedAfterAnother = new BenchView(false, false, false, 0, Empty, 2, BatchActive: true, BatchSuccess: 2);
        Assert.Equal(2, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(closedAfterAnother, 1.1, PlankNext)).Crafts);
    }

    [Fact]
    public void A_close_that_posts_its_cancelled_craft_as_failed_counts_nothing_extra()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 2, RunBench, BatchActive: true, BatchSuccess: 2, IsCrafting: true), 1, PlankNext);

        var closedWithCancelledFailure = new BenchView(false, false, false, 0, Empty, 3, BatchActive: true, BatchSuccess: 2);
        Assert.Equal(2, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(closedWithCancelledFailure, 1.1, PlankNext)).Crafts);
    }

    [Fact]
    public void A_batch_flag_with_nothing_crafting_is_taken_as_stale_and_crafted_past()
    {
        var runner = ResumedRunner();
        var staleBatch = new BenchView(true, true, false, 3, LogsLaidOut, 2, RunBench, BatchActive: true);

        Assert.IsType<RunnerCommand.None>(runner.Tick(staleBatch with { Contents = Empty, MaxCrafts = 0 }, 11.25, PlankNext));
        Assert.IsType<RunnerCommand.Notice>(runner.Tick(staleBatch with { Contents = Empty, MaxCrafts = 0 }, 11.25 + QueueRunner.StaleBatchSeconds, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(staleBatch with { Contents = Empty, MaxCrafts = 0 }, 14.5, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(staleBatch, 14.75, PlankNext));
        Assert.Equal(new RunnerCommand.Craft(3), runner.Tick(staleBatch, 15, PlankNext));
    }

    [Fact]
    public void A_live_batch_is_waited_out_and_a_long_unchanged_wait_is_shown_once()
    {
        var runner = ResumedRunner();
        var live = new BenchView(true, true, false, 0, Empty, 1, RunBench, BatchActive: true, IsCrafting: true);

        Assert.IsType<RunnerCommand.None>(runner.Tick(live, 11.25, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(live, 11.25 + QueueRunner.StaleBatchSeconds + 1, PlankNext));

        var notice = Assert.IsType<RunnerCommand.Notice>(runner.Tick(live, 11.25 + QueueRunner.BusyNoticeSeconds, PlankNext));
        Assert.Contains("waiting", notice.Text);
        Assert.IsType<RunnerCommand.None>(runner.Tick(live, 50, PlankNext));
        Assert.True(runner.IsRunning);
    }

    [Fact]
    public void The_order_a_step_counts_for_is_known_only_while_that_step_is_in_progress()
    {
        var runner = new QueueRunner();
        NextStep ForOrderSeven() => new NextStep.Craft(PlankStep, IsTarget: true, OrderId: 7);

        runner.Start();

        Assert.Null(runner.CraftingFor);

        runner.Tick(Idle(), 0, ForOrderSeven);

        Assert.Equal((long?)7, runner.CraftingFor);

        runner.Tick(Closed, 1, ForOrderSeven);

        Assert.Null(runner.CraftingFor);
    }

    [Fact]
    public void Craft_now_goes_first_between_steps_second_during_one_and_nowhere_while_paused()
    {
        var runner = new QueueRunner();

        Assert.Equal(CraftNowPlace.Front, runner.CraftNowPlace);

        runner.Start();

        Assert.Equal(CraftNowPlace.Front, runner.CraftNowPlace);

        runner.Tick(Idle(), 0, PlankNext);

        Assert.Equal(CraftNowPlace.BehindHead, runner.CraftNowPlace);

        runner.Tick(Closed, 1, PlankNext);

        Assert.Equal(CraftNowPlace.Refused, runner.CraftNowPlace);
    }

    [Fact]
    public void Craft_now_pressed_mid_step_leaves_that_step_to_its_own_order_and_is_made_next()
    {
        var queue = new CraftQueue();
        var runner = new QueueRunner();
        var stock = Bench.Mats((Bench.Log, 10), (Bench.Plank, 4), (Bench.Rope, 1));
        NextStep Next() => QueuePlanner.Next(queue.Head, Bench.Book(), stock, Array.Empty<KeepTarget>(), 4, Bench.Name);

        queue.Add("plank", "Plank", 3);
        runner.Start();
        runner.Tick(Idle(), 0, Next);
        runner.Tick(Idle(LogsLaidOut, 3), 0.1, Next);
        Assert.Equal(new RunnerCommand.Craft(3), runner.Tick(Idle(LogsLaidOut, 3), 0.5, Next));

        queue.PushFront("crate", "Crate", 1, behindHead: runner.CraftNowPlace == CraftNowPlace.BehindHead);
        Assert.Equal("plank", queue.Head?.Key);
        Assert.IsType<RunnerCommand.ConfirmResult>(runner.Tick(new BenchView(true, false, true, 0, Empty, 3), 1, Next));

        var done = Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Idle(), 1.1, Next));
        queue.Progress(done.OrderId, done.Crafts);

        Assert.Equal(new[] { ("crate", 1) }, queue.Entries.Select(entry => (entry.Key, entry.Crafts)));
        Assert.Equal(new RunnerCommand.ClickRecipe("crate"), runner.Tick(Idle(), 1.2, Next));
    }

    [Fact]
    public void A_single_craft_closed_mid_craft_counts_nothing_from_an_earlier_batch()
    {
        var runner = new QueueRunner();
        var single = new NextStep.Craft(new CraftStep(Bench.PlankRecipe, 1), IsTarget: true);

        runner.Start();
        runner.Tick(Idle(), 0, () => single);
        runner.Tick(Idle(LogsLaidOut, 3), 0.1, () => single);
        Assert.Equal(new RunnerCommand.Craft(1), runner.Tick(Idle(LogsLaidOut, 3), 0.5, () => single));
        runner.Tick(new BenchView(true, true, false, 0, Empty, 4, RunBench, BatchActive: true, IsCrafting: true), 1, () => single);

        Assert.IsType<RunnerCommand.Paused>(runner.Tick(new BenchView(false, false, false, 0, Empty, 4), 1.1, () => single));
    }

    [Fact]
    public void A_craft_the_close_cancelled_counts_nothing()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, true, false, 0, Empty, 0, RunBench), 1, PlankNext);

        Assert.IsType<RunnerCommand.Paused>(runner.Tick(Closed, 1.1, PlankNext));
    }

    [Fact]
    public void A_result_on_screen_when_the_workbench_closes_counts_the_whole_step()
    {
        var runner = CraftingRunner();

        runner.Tick(new BenchView(true, false, true, 0, Empty, 0, RunBench), 1, PlankNext);

        Assert.Equal(3, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Closed, 1.1, PlankNext)).Crafts);
    }

    [Fact]
    public void An_empty_queue_stops_as_done()
    {
        var runner = new QueueRunner();

        runner.Start();

        Assert.True(Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), 0, () => new NextStep.Finished())).IsDone);
    }

    [Fact]
    public void Endless_intermediates_without_a_finished_target_stop_the_run()
    {
        var runner = new QueueRunner();
        RunnerCommand last = new RunnerCommand.None();
        var now = 0.0;
        NextStep Intermediate() => new NextStep.Craft(PlankStep, IsTarget: false);

        runner.Start();

        for (var batch = 0; batch <= QueueRunner.MaxIntermediateBatches && last is not RunnerCommand.Stopped; batch++)
        {
            last = runner.Tick(Idle(), now += 1, Intermediate);

            if (last is RunnerCommand.Stopped) break;

            runner.Tick(Idle(LogsLaidOut, 3), now += 0.1, Intermediate);
            runner.Tick(Idle(LogsLaidOut, 3), now += 0.5, Intermediate);
            runner.Tick(new BenchView(true, false, true, 0, Empty, 3), now += 1, Intermediate);
            runner.Tick(Idle(), now += 0.1, Intermediate);
        }

        Assert.IsType<RunnerCommand.Stopped>(last);
    }

    [Fact]
    public void A_stopped_runner_does_nothing()
    {
        Assert.IsType<RunnerCommand.None>(new QueueRunner().Tick(Idle(), 0, PlankNext));
    }

    /// <summary>A paused run whose workbench reopened, now about to pick its next step.</summary>
    private static QueueRunner ResumedRunner()
    {
        var runner = PausedRunner();

        runner.Tick(Idle(), 10, PlankNext);
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(), 10 + QueueRunner.ResumeSettleSeconds, PlankNext));

        return runner;
    }

    private static QueueRunner CraftingRunner(bool resumesOnReopen = true, bool resumesAtOtherBenches = false)
    {
        var runner = new QueueRunner(resumesOnReopen, resumesAtOtherBenches);

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);
        runner.Tick(Idle(LogsLaidOut, 3), 0.1, PlankNext);
        runner.Tick(Idle(LogsLaidOut, 3), 0.5, PlankNext);

        return runner;
    }

    /// <summary>A run at <see cref="RunBench"/> whose craft the close cancelled, so nothing was made.</summary>
    private static QueueRunner PausedRunner(bool resumesAtOtherBenches = false)
    {
        var runner = CraftingRunner(resumesAtOtherBenches: resumesAtOtherBenches);

        runner.Tick(Closed, 1, PlankNext);

        return runner;
    }
}

public class PageCommandTests
{
    [Fact]
    public void Recipe_keys_survive_encoding_even_with_separators_inside()
    {
        var command = PageCommand.Parse(PageCommand.Prefix + "add|" + Uri.EscapeDataString("101|202,3") + "|4");

        Assert.Equal(new PageCommand.Add("101|202,3", 4), command);
    }

    [Fact]
    public void Craft_now_carries_its_quantity()
    {
        Assert.Equal(new PageCommand.CraftNow("crate", 2), PageCommand.Parse(PageCommand.Prefix + "chain|crate|2"));
    }

    [Fact]
    public void A_zero_or_negative_quantity_is_rejected()
    {
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "add|crate|0"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "add|crate|-1"));
    }

    [Fact]
    public void Diagnostics_keep_their_pipes()
    {
        Assert.Equal(new PageCommand.Diagnostic("a|b"), PageCommand.Parse(PageCommand.Prefix + "diag|a|b"));
    }

    [Fact]
    public void A_keep_count_carries_its_recipe_and_zero_means_stop_keeping()
    {
        Assert.Equal(new PageCommand.Keep("101|202", 10), PageCommand.Parse(PageCommand.Prefix + "keep|" + Uri.EscapeDataString("101|202") + "|10"));
        Assert.Equal(new PageCommand.Keep("rope", 0), PageCommand.Parse(PageCommand.Prefix + "keep|rope|0"));
    }

    [Fact]
    public void A_keep_count_out_of_range_is_rejected()
    {
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "keep|rope|-1"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "keep|rope|" + (KeepTarget.MaxCount + 1)));
    }

    [Fact]
    public void Unkeep_names_the_item()
    {
        Assert.Equal(new PageCommand.Unkeep(4), PageCommand.Parse(PageCommand.Prefix + "unkeep|4"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "unkeep|0"));
    }

    [Fact]
    public void A_move_names_the_order_by_id_and_its_new_place()
    {
        Assert.Equal(new PageCommand.Move(7, 2), PageCommand.Parse(PageCommand.Prefix + "move|7|2"));
        Assert.Equal(new PageCommand.Move(7, 0), PageCommand.Parse(PageCommand.Prefix + "move|7|0"));
    }

    [Fact]
    public void A_move_without_a_real_order_or_place_is_rejected()
    {
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "move|0|1"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "move|7|-1"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "move|7"));
    }

    [Fact]
    public void A_remove_names_the_order_by_its_id()
    {
        Assert.Equal(new PageCommand.Remove(9), PageCommand.Parse(PageCommand.Prefix + "remove|9"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "remove|0"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "remove|-1"));
    }

    [Fact]
    public void An_amount_sets_an_orders_crafts_and_never_to_zero()
    {
        Assert.Equal(new PageCommand.Amount(7, 3), PageCommand.Parse(PageCommand.Prefix + "amount|7|3"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "amount|7|0"));
        Assert.IsType<PageCommand.Unknown>(PageCommand.Parse(PageCommand.Prefix + "amount|0|3"));
    }

    [Fact]
    public void Other_mods_messages_are_not_ours()
    {
        Assert.False(PageCommand.IsOurs("slmod|storagehub|ready"));
    }
}

public class HubViewTests
{
    [Fact]
    public void The_view_is_json_the_page_can_read_with_hostile_names_escaped()
    {
        var json = HubView.Build(new Dictionary<string, string> { ["crate"] = "Makes first: \"Plank\" ×2" }, new[] { new QueueEntry("crate", "Crate</script>", 2) }, true, "line\nbreak", NoKeep);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Makes first: \"Plank\" ×2", parsed.RootElement.GetProperty("marks").GetProperty("crate").GetString());
        Assert.Equal("Crate</script>", parsed.RootElement.GetProperty("queue")[0].GetProperty("name").GetString());
        Assert.True(parsed.RootElement.GetProperty("running").GetBoolean());
        Assert.DoesNotContain("</script>", json);
    }

    [Fact]
    public void The_page_gets_every_target_and_the_count_kept_for_each_recipe()
    {
        var keep = new KeepView(true, new[] { new KeepTarget(Bench.Rope, "rope", "Rope \"tough\"", 5) }, new Dictionary<string, int> { ["rope"] = 5 });

        var json = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", keep);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var shown = parsed.RootElement.GetProperty("keep");
        Assert.True(shown.GetProperty("on").GetBoolean());
        Assert.Equal(Bench.Rope, shown.GetProperty("targets")[0].GetProperty("item").GetInt32());
        Assert.Equal("Rope \"tough\"", shown.GetProperty("targets")[0].GetProperty("name").GetString());
        Assert.Equal(5, shown.GetProperty("targets")[0].GetProperty("count").GetInt32());
        Assert.Equal(5, shown.GetProperty("byKey").GetProperty("rope").GetInt32());
    }

    [Fact]
    public void Each_order_carries_its_id_and_where_it_stands_and_the_queue_its_totals()
    {
        var queue = new[] { new QueueEntry("crate", "Crate", 1, Id: 4), new QueueEntry("rope", "Rope", 1, Id: 9) };
        var outlook = new QueueOutlook(
            new[] { new OrderOutlook(4, OrderState.MakesFirst, "Makes first: Plank x2") },
            new[] { new MaterialTotal(Bench.Fibre, "Fibre", 3, 1, 2) },
            "Heads up: Crate is short of Fibre x2. The queue will stop there.");

        var json = HubView.Build(new Dictionary<string, string>(), queue, false, "", NoKeep, outlook: outlook);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Assert.Equal(4, root.GetProperty("queue")[0].GetProperty("id").GetInt64());
        Assert.Equal("via", root.GetProperty("queue")[0].GetProperty("state").GetString());
        Assert.Equal("Makes first: Plank x2", root.GetProperty("queue")[0].GetProperty("note").GetString());
        Assert.Equal("", root.GetProperty("queue")[1].GetProperty("state").GetString());
        Assert.Equal("Heads up: Crate is short of Fibre x2. The queue will stop there.", root.GetProperty("headsUp").GetString());
        Assert.Equal(2, root.GetProperty("totals")[0].GetProperty("short").GetInt32());
        Assert.Equal(3, root.GetProperty("totals")[0].GetProperty("need").GetInt32());
    }

    [Fact]
    public void Only_a_running_queue_can_show_as_paused()
    {
        var idle = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", NoKeep, isPaused: true);
        var running = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), true, "", NoKeep, isPaused: true);

        using var idleParsed = System.Text.Json.JsonDocument.Parse(idle);
        using var runningParsed = System.Text.Json.JsonDocument.Parse(running);
        Assert.False(idleParsed.RootElement.GetProperty("paused").GetBoolean());
        Assert.True(runningParsed.RootElement.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public void Only_a_running_queue_names_the_order_being_crafted()
    {
        var idle = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", NoKeep, activeOrderId: 7);
        var running = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), true, "", NoKeep, activeOrderId: 7);

        using var idleParsed = System.Text.Json.JsonDocument.Parse(idle);
        using var runningParsed = System.Text.Json.JsonDocument.Parse(running);
        Assert.Equal(0, idleParsed.RootElement.GetProperty("activeId").GetInt64());
        Assert.Equal(7, runningParsed.RootElement.GetProperty("activeId").GetInt64());
    }

    [Fact]
    public void Only_a_running_queue_sends_the_games_craft_speed_and_never_a_speed_that_is_not_a_number()
    {
        static double SpeedOf(string json)
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(json);

            return parsed.RootElement.GetProperty("craftSpeed").GetDouble();
        }

        Assert.Equal(0.2, SpeedOf(HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), true, "", NoKeep, craftSpeed: 0.2f)), 6);
        Assert.Equal(0, SpeedOf(HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", NoKeep, craftSpeed: 0.2f)));
        Assert.Equal(0, SpeedOf(HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), true, "", NoKeep, craftSpeed: float.NaN)));
        Assert.Equal(0, SpeedOf(HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), true, "", NoKeep, craftSpeed: float.PositiveInfinity)));
    }

    [Fact]
    public void Only_recipes_that_make_more_than_one_list_their_yield()
    {
        var yields = new Dictionary<string, int> { ["plank"] = 2, ["rope"] = 1 };

        var json = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", NoKeep, yields: yields);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var shown = parsed.RootElement.GetProperty("yields");
        Assert.Equal(2, shown.GetProperty("plank").GetInt32());
        Assert.False(shown.TryGetProperty("rope", out _));
    }

    [Fact]
    public void A_kept_item_shows_how_many_the_home_holds_and_null_when_it_could_not_be_counted()
    {
        var targets = new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5), new KeepTarget(Bench.Plank, "plank", "Plank", 9) };
        var keep = new KeepView(true, targets, new Dictionary<string, int>(), new Dictionary<int, int> { [Bench.Rope] = 3 });

        var json = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", keep);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var shown = parsed.RootElement.GetProperty("keep").GetProperty("targets");
        Assert.Equal(3, shown[0].GetProperty("have").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, shown[1].GetProperty("have").ValueKind);
    }

    /// <summary>
    /// Every part of the view, written out by hand from what the page reads: keys in order, an uncosted order with no
    /// state, yields of one and free stamina costs left out, an uncounted target as null, dyes grouped by plain key.
    /// </summary>
    [Fact]
    public void The_whole_view_is_written_exactly_as_the_page_reads_it()
    {
        var marks = new Dictionary<string, string> { ["b"] = "y", ["a"] = "x" };
        var queue = new[] { new QueueEntry("crate", "Crate", 2, Id: 4), new QueueEntry("rope", "Rope", 1, IsTopUp: true, Id: 9) };
        var outlook = new QueueOutlook(new[] { new OrderOutlook(4, OrderState.Ready, "Ready") }, new[] { new MaterialTotal(Bench.Fibre, "Fibre", 3, 1, 2) }, "Heads up");
        var keep = new KeepView(true, new[] { new KeepTarget(5, "plank", "Plank", 10), new KeepTarget(6, "rope", "Rope", 2) },
            new Dictionary<string, int> { ["rope"] = 2, ["plank"] = 10 }, new Dictionary<int, int> { [5] = 3 });
        var stamina = new StaminaView(3, false, new Dictionary<string, int> { ["rope"] = 0, ["plank"] = 8 });
        var dyes = new[]
        {
            new DyeOption("chair", "chair_red", 11, "#ff0000", "Red", 1, 2),
            new DyeOption("bed", "bed_blue", 12, "#0000ff", "Blue", 1, 0),
            new DyeOption("chair", "chair_blue", 12, "#0000ff", "Blue", 1, 0),
        };

        var json = HubView.Build(marks, queue, true, "go", keep, isPaused: true, outlook, new Dictionary<string, int> { ["rope"] = 1, ["plank"] = 2 }, 4, stamina, takesUnitsOffStacks: true, dyes, craftSpeed: 0.25f);

        Assert.Equal(
            """{"marks":{"a":"x","b":"y"},"queue":[{"id":4,"key":"crate","name":"Crate","crafts":2,"topUp":false,"state":"ready","note":"Ready"},{"id":9,"key":"rope","name":"Rope","crafts":1,"topUp":true,"state":"","note":""}],"running":true,"paused":true,"activeId":4,"craftSpeed":0.25,"status":"go","headsUp":"Heads up","totals":[{"item":3,"name":"Fibre","need":3,"have":1,"short":2}],"yields":{"plank":2},"keep":{"on":true,"targets":[{"item":5,"key":"plank","name":"Plank","count":10,"have":3},{"item":6,"key":"rope","name":"Rope","count":2,"have":null}],"byKey":{"plank":10,"rope":2}},"stamina":{"have":3,"out":false,"cost":{"plank":8}},"takesUnits":true,"dyes":{"bed":[{"key":"bed_blue","dye":12,"hex":"#0000ff","color":"Blue","need":1,"have":0}],"chair":[{"key":"chair_red","dye":11,"hex":"#ff0000","color":"Red","need":1,"have":2},{"key":"chair_blue","dye":12,"hex":"#0000ff","color":"Blue","need":1,"have":0}]}}""",
            json);
    }

    [Fact]
    public void A_small_view_built_after_a_big_one_carries_nothing_over()
    {
        var marks = new Dictionary<string, string> { ["crate"] = "Makes first: Plank x2" };

        HubView.Build(marks, new[] { new QueueEntry("crate", "Crate", 2, Id: 4) }, true, "a long status line", NoKeep, craftSpeed: 0.5f);
        var json = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", NoKeep);

        Assert.Equal(
            """{"marks":{},"queue":[],"running":false,"paused":false,"activeId":0,"craftSpeed":0,"status":"","headsUp":"","totals":[],"yields":{},"keep":{"on":false,"targets":[],"byKey":{}},"stamina":null,"takesUnits":false,"dyes":{}}""",
            json);
    }

    private static readonly KeepView NoKeep = new(false, Array.Empty<KeepTarget>(), new Dictionary<string, int>());
}

public class TopUpPlannerTests
{
    private const int Net = 7;

    private static readonly Recipe NetRecipe = new("net", "Net", Net, Yield: 1, Bench.Mats((Bench.Fibre, 3)), IsLocked: false);

    [Fact]
    public void A_low_item_is_topped_up_to_its_target()
    {
        var plan = Plan(new[] { Keep(Bench.Rope, "rope", 5) }, Bench.Mats((Bench.Fibre, 30)), Bench.Mats((Bench.Rope, 2)));

        Assert.Equal(new[] { ("rope", 3) }, Orders(plan));
    }

    [Fact]
    public void Nothing_is_queued_when_the_home_already_holds_the_target()
    {
        var plan = Plan(new[] { Keep(Bench.Rope, "rope", 5) }, Bench.Mats((Bench.Fibre, 30)), Bench.Mats((Bench.Rope, 5)));

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void What_the_queue_already_makes_counts_against_the_target()
    {
        var targets = new[] { Keep(Bench.Rope, "rope", 5) };

        var partly = Plan(targets, Bench.Mats((Bench.Fibre, 30)), Bench.Mats((Bench.Rope, 2)), new QueueEntry("rope", "Rope", 2));
        var fully = Plan(targets, Bench.Mats((Bench.Fibre, 30)), Bench.Mats((Bench.Rope, 2)), new QueueEntry("rope", "Rope", 3));

        Assert.Equal(new[] { ("rope", 1) }, Orders(partly));
        Assert.True(fully.IsEmpty);
    }

    [Fact]
    public void Orders_are_capped_by_the_materials_in_the_house()
    {
        var plan = Plan(new[] { Keep(Bench.Rope, "rope", 10) }, Bench.Mats((Bench.Fibre, 9)), Bench.Mats((Bench.Rope, 0)));

        Assert.Equal(new[] { ("rope", 3) }, Orders(plan));
    }

    [Fact]
    public void A_target_with_no_materials_is_reported_and_not_queued()
    {
        var plan = Plan(new[] { Keep(Bench.Rope, "rope", 5) }, Bench.Mats((Bench.Fibre, 2)), Bench.Mats((Bench.Rope, 0)));

        Assert.Empty(plan.Orders);
        Assert.Equal(new[] { "Rope" }, plan.Short);
        Assert.Contains("not enough materials for Rope", TopUpPlanner.Describe(plan));
    }

    [Fact]
    public void Materials_the_queue_will_use_are_not_planned_twice()
    {
        var plan = Plan(new[] { Keep(Bench.Rope, "rope", 5) }, Bench.Mats((Bench.Fibre, 9)), Bench.Mats((Bench.Rope, 0)), new QueueEntry("net", "Net", 2));

        Assert.Equal(new[] { ("rope", 1) }, Orders(plan));
    }

    [Fact]
    public void Two_targets_share_the_materials_instead_of_both_counting_them()
    {
        var targets = new[] { Keep(Bench.Rope, "rope", 2), Keep(Net, "net", 2) };

        var plan = Plan(targets, Bench.Mats((Bench.Fibre, 9)), Bench.Mats((Bench.Rope, 0), (Net, 0)));

        Assert.Equal(new[] { ("rope", 2), ("net", 1) }, Orders(plan));
    }

    [Fact]
    public void A_later_target_cannot_use_up_what_an_earlier_one_keeps()
    {
        var targets = new[] { Keep(Bench.Plank, "plank", 4), Keep(Bench.Crate, "crate", 1) };

        var plan = Plan(targets, Bench.Mats((Bench.Log, 2), (Bench.Rope, 1)), Bench.Mats((Bench.Plank, 0), (Bench.Crate, 0)));

        Assert.Equal(new[] { ("plank", 2) }, Orders(plan));
        Assert.Equal(new[] { "Crate" }, plan.Short);
    }

    [Fact]
    public void Running_the_orders_leaves_every_kept_item_at_its_target()
    {
        var targets = new[] { Keep(Bench.Plank, "plank", 4), Keep(Bench.Crate, "crate", 1) };
        var house = Bench.Mats((Bench.Log, 4), (Bench.Rope, 1));

        var plan = Plan(targets, house, Bench.Mats((Bench.Plank, 0), (Bench.Crate, 0)));
        Run(plan, targets, house);

        Assert.Equal(new[] { ("plank", 2), ("crate", 1) }, Orders(plan));
        Assert.Equal((4, 1), (house[Bench.Plank], house[Bench.Crate]));
    }

    [Fact]
    public void A_full_kept_item_is_not_used_up_to_top_up_another()
    {
        var targets = new[] { Keep(Bench.Plank, "plank", 4), Keep(Bench.Crate, "crate", 1) };
        var house = Bench.Mats((Bench.Plank, 4), (Bench.Log, 2), (Bench.Rope, 1));

        var plan = Plan(targets, house, Bench.Mats((Bench.Plank, 4), (Bench.Crate, 0)));
        Run(plan, targets, house);

        Assert.Equal(new[] { ("crate", 1) }, Orders(plan));
        Assert.Equal((4, 1), (house[Bench.Plank], house[Bench.Crate]));
    }

    [Fact]
    public void Crafts_round_up_when_a_recipe_makes_several()
    {
        var plan = Plan(new[] { Keep(Bench.Plank, "plank", 5) }, Bench.Mats((Bench.Log, 10)), Bench.Mats((Bench.Plank, 0)));

        Assert.Equal(new[] { ("plank", 3) }, Orders(plan));
    }

    [Fact]
    public void Intermediates_are_planned_for_a_target_that_needs_them()
    {
        var plan = Plan(new[] { Keep(Bench.Crate, "crate", 1) }, Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3)), Bench.Mats((Bench.Crate, 0)));

        Assert.Equal(new[] { ("crate", 1) }, Orders(plan));
    }

    [Fact]
    public void A_locked_or_missing_recipe_is_left_alone()
    {
        var lockedRope = Bench.RopeRecipe with { IsLocked = true };
        var book = new RecipeBook(new[] { Bench.PlankRecipe, lockedRope, Bench.CrateRecipe });
        var targets = new[] { Keep(Bench.Rope, "rope", 5), Keep(Net, "net", 5) };

        var plan = TopUpPlanner.Plan(targets, book, Bench.Mats((Bench.Fibre, 30)), Bench.Mats((Bench.Rope, 0), (Net, 0)), Array.Empty<QueueEntry>(), maxDepth: 4);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void A_target_whose_home_count_is_unknown_is_skipped()
    {
        var plan = Plan(new[] { Keep(Bench.Rope, "rope", 5) }, Bench.Mats((Bench.Fibre, 30)), Bench.Mats());

        Assert.True(plan.IsEmpty);
    }

    private static KeepTarget Keep(int itemId, string key, int count) => new(itemId, key, NameOf(itemId), count);

    private static string NameOf(int itemId) => itemId switch
    {
        Net => "Net",
        Bench.Crate => "Crate",
        _ => Bench.Name(itemId),
    };

    private static TopUpPlan Plan(KeepTarget[] targets, Dictionary<int, int> stock, Dictionary<int, int> held, params QueueEntry[] queue) =>
        TopUpPlanner.Plan(targets, Bench.Book(NetRecipe), stock, held, queue, maxDepth: 4);

    private static (string, int)[] Orders(TopUpPlan plan) => plan.Orders.Select(order => (order.Recipe.Key, order.Crafts)).ToArray();

    /// <summary>Queues the plan and runs it the way the game does: one step at a time, re-planned from the house as it stands.</summary>
    private static void Run(TopUpPlan plan, KeepTarget[] targets, Dictionary<int, int> house)
    {
        var book = Bench.Book(NetRecipe);
        var queue = new CraftQueue();

        foreach (var order in plan.Orders) queue.Add(order.Recipe.Key, order.Recipe.Name, order.Crafts, isTopUp: true);

        for (var steps = 0; steps < 20; steps++)
        {
            var next = QueuePlanner.Next(queue.Head, book, house, targets, 4, Bench.Name);

            if (next is NextStep.Finished) return;

            var craft = Assert.IsType<NextStep.Craft>(next);
            Make(house, craft.Step);

            if (craft.IsTarget) queue.Progress(craft.OrderId, craft.Step.Crafts);
        }

        Assert.Fail("the queue never finished");
    }

    private static void Make(Dictionary<int, int> house, CraftStep step)
    {
        foreach (var (itemId, perCraft) in step.Recipe.Materials) house[itemId] = house.GetValueOrDefault(itemId) - perCraft * step.Crafts;

        house[step.Recipe.ProductId] = house.GetValueOrDefault(step.Recipe.ProductId) + step.Crafts * step.Recipe.Yield;
    }
}

public class TopUpTimingTests
{
    [Fact]
    public void A_bench_is_checked_once_after_it_settles()
    {
        var timing = new TopUpTiming();

        Assert.False(timing.IsDue(true, 0, Idle));
        Assert.False(timing.IsDue(true, 1, Idle));
        Assert.True(timing.IsDue(true, TopUpTiming.SettleSeconds, Idle));
        Assert.False(timing.IsDue(true, 5, Idle));
        Assert.False(timing.IsDue(true, 60, Idle));
    }

    [Fact]
    public void Opening_the_bench_again_checks_again()
    {
        var timing = new TopUpTiming();

        timing.IsDue(true, 0, Idle);
        timing.IsDue(true, 2, Idle);
        timing.IsDue(false, 3, Idle);

        Assert.False(timing.IsDue(true, 4, Idle));
        Assert.True(timing.IsDue(true, 4 + TopUpTiming.SettleSeconds, Idle));
    }

    [Fact]
    public void A_changed_target_is_checked_without_reopening()
    {
        var timing = new TopUpTiming();

        timing.IsDue(true, 0, Idle);
        timing.IsDue(true, 2, Idle);
        timing.Request();

        Assert.True(timing.IsDue(true, 2.1, Idle));
    }

    [Fact]
    public void A_closed_bench_is_never_due()
    {
        var timing = new TopUpTiming();

        timing.Request();

        Assert.False(timing.IsDue(false, 10, Idle));
    }

    [Fact]
    public void A_check_that_finds_the_bench_busy_is_dropped_not_fired_when_the_run_ends()
    {
        var timing = new TopUpTiming();

        timing.IsDue(true, 0, Busy);

        Assert.False(timing.IsDue(true, TopUpTiming.SettleSeconds, Busy));
        Assert.False(timing.IsDue(true, 30, Idle));
    }

    private static bool Idle() => true;

    private static bool Busy() => false;
}

public class KeepTargetsTests
{
    [Fact]
    public void Setting_zero_stops_keeping_the_item()
    {
        var targets = new KeepTargets(new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) });

        targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 0));

        Assert.Empty(targets.All);
    }

    [Fact]
    public void An_item_has_one_target_and_the_latest_wins()
    {
        var targets = new KeepTargets();

        targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 5));
        targets.Set(new KeepTarget(Bench.Rope, "rope-alt", "Rope", 8));

        Assert.Equal(new[] { new KeepTarget(Bench.Rope, "rope-alt", "Rope", 8) }, targets.All);
    }

    [Fact]
    public void Counts_are_capped()
    {
        var targets = new KeepTargets();

        targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 50000));

        Assert.Equal(KeepTarget.MaxCount, targets.All[0].Count);
    }

    [Fact]
    public void Every_recipe_that_makes_a_kept_item_shows_its_count()
    {
        var ropeAlt = Bench.RopeRecipe with { Key = "rope-alt" };
        var targets = new KeepTargets(new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) });

        var byKey = targets.ByRecipe(Bench.Book(ropeAlt));

        Assert.Equal(new[] { ("rope", 5), ("rope-alt", 5) }, byKey.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value)));
    }

    [Fact]
    public void Removing_an_item_stops_keeping_it()
    {
        var targets = new KeepTargets(new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5), new KeepTarget(Bench.Plank, "plank", "Plank", 9) });

        targets.Remove(Bench.Rope);

        Assert.Equal(new[] { Bench.Plank }, targets.All.Select(target => target.ItemId));
    }

    [Fact]
    public void Every_change_to_the_targets_moves_their_version_and_a_change_to_nothing_does_not()
    {
        var targets = new KeepTargets();
        var seen = targets.Version;

        void Moved(Action change)
        {
            change();
            Assert.NotEqual(seen, targets.Version);
            seen = targets.Version;
        }

        void Still(Action change)
        {
            change();
            Assert.Equal(seen, targets.Version);
        }

        Moved(() => targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 5)));
        Moved(() => targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 8)));
        Moved(() => targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 0)));
        Still(() => targets.Set(new KeepTarget(Bench.Plank, "plank", "Plank", 0)));
        Still(() => targets.Remove(Bench.Plank));
        Moved(() => targets.Set(new KeepTarget(Bench.Plank, "plank", "Plank", 3)));
        Moved(() => targets.Remove(Bench.Plank));
    }
}

public class QueueVersionTests
{
    [Fact]
    public void Every_change_to_the_queue_moves_its_version_and_a_change_to_nothing_does_not()
    {
        var queue = new CraftQueue();
        var seen = queue.Version;

        void Moved(Action change)
        {
            change();
            Assert.NotEqual(seen, queue.Version);
            seen = queue.Version;
        }

        void Still(Action change)
        {
            change();
            Assert.Equal(seen, queue.Version);
        }

        Still(() => queue.Clear());
        Still(() => queue.Add("plank", "Plank", 0));
        Moved(() => queue.Add("plank", "Plank", 2));
        Moved(() => queue.Add("plank", "Plank", 1));
        Moved(() => queue.PushFront("rope", "Rope", 1));
        Still(() => queue.PushFront("rope", "Rope", 0));

        var plank = queue.Entries[1].Id;

        Moved(() => queue.Move(plank, 0));
        Still(() => queue.Move(999, 0));
        Moved(() => queue.SetCrafts(plank, 5));
        Still(() => queue.SetCrafts(plank, 0));
        Still(() => queue.SetCrafts(999, 5));
        Moved(() => queue.Progress(plank, 2));
        Still(() => queue.Progress(999, 1));
        Still(() => queue.Progress(plank, 0));
        Still(() => queue.Remove(999));
        Moved(() => queue.Remove(plank));
        Moved(() => queue.Clear());
    }

    [Fact]
    public void An_order_finished_by_its_last_crafts_moves_the_version_as_it_leaves()
    {
        var queue = new CraftQueue();

        queue.Add("plank", "Plank", 2);

        var before = queue.Version;

        queue.Progress(queue.Entries[0].Id, 2);

        Assert.Empty(queue.Entries);
        Assert.NotEqual(before, queue.Version);
    }
}

/// <summary>What the panel is drawn from, as HubSession gathers it each tick.</summary>
internal static class Panel
{
    private static readonly IReadOnlyDictionary<string, string> NoMarks = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, int> NoCounts = new Dictionary<string, int>();
    private static readonly IReadOnlyList<string> Keys = new[] { "plank", "rope" };

    public static ViewInputs Of(
        CraftQueue queue,
        KeepTargets targets,
        string status = "",
        bool isRunning = false,
        StaminaGist? stamina = null,
        IReadOnlyDictionary<string, string>? marks = null,
        IReadOnlyList<DyeOption>? dyes = null) =>
        new(marks ?? NoMarks, queue, queue.Version, isRunning, false, queue.Head?.Id ?? 0, status, true, targets, targets.Version, NoCounts, null,
            QueueOutlook.Empty, NoCounts, Keys, stamina, false, dyes ?? Array.Empty<DyeOption>());
}

public class ViewGateTests
{
    private readonly CraftQueue _queue = new();
    private readonly KeepTargets _targets = new();
    private readonly ViewGate _gate = new();

    private string? Next(string status = "", StaminaGist? stamina = null, IReadOnlyDictionary<string, string>? marks = null, IReadOnlyList<DyeOption>? dyes = null) =>
        _gate.Next(Panel.Of(_queue, _targets, status, stamina: stamina, marks: marks, dyes: dyes));

    [Fact]
    public void The_first_view_is_sent_and_the_same_one_after_it_is_not()
    {
        Assert.NotNull(Next());
        Assert.Null(Next());
        Assert.Null(Next());
    }

    /// <summary>
    /// Proves equal inputs are not built at all, not just not sent: a list changed behind the gate's back would build a
    /// different view, and none is sent.
    /// </summary>
    [Fact]
    public void Equal_inputs_are_not_built_again()
    {
        var dyes = new List<DyeOption>();

        Next(dyes: dyes);
        dyes.Add(new DyeOption("chair", "chair_red", 11, "#ff0000", "Red", 1, 2));

        Assert.Null(Next(dyes: dyes));
    }

    [Fact]
    public void A_new_status_line_is_sent()
    {
        Next("Crafting Plank");

        Assert.Contains("\"status\":\"Done\"", Next("Done"));
    }

    [Fact]
    public void A_change_to_the_queue_in_place_is_sent()
    {
        Next();
        _queue.Add("crate", "Crate", 2);

        Assert.Contains("\"key\":\"crate\"", Next());

        _queue.SetCrafts(_queue.Entries[0].Id, 5);

        Assert.Contains("\"crafts\":5", Next());
    }

    [Fact]
    public void A_new_keep_target_is_sent()
    {
        Next();
        _targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 5));

        Assert.Contains("\"count\":5", Next());
    }

    [Fact]
    public void Stamina_is_sent_when_its_whole_number_changes_and_not_for_a_fraction()
    {
        Next(stamina: StaminaGist.Of(new StaminaRead(20.2f, 8), hasQueue: false));

        Assert.Null(Next(stamina: StaminaGist.Of(new StaminaRead(20.7f, 8), hasQueue: false)));
        Assert.Contains("\"have\":21", Next(stamina: StaminaGist.Of(new StaminaRead(21.1f, 8), hasQueue: false)));
    }

    [Fact]
    public void A_page_loaded_again_is_sent_the_same_view_again()
    {
        var first = Next("Ready");

        _gate.Forget();

        Assert.Equal(first, Next("Ready"));
    }

    [Fact]
    public void The_same_view_from_new_but_equal_inputs_is_not_sent_again()
    {
        Next(marks: new Dictionary<string, string> { ["crate"] = "Makes first: Plank x2" });

        Assert.Null(Next(marks: new Dictionary<string, string> { ["crate"] = "Makes first: Plank x2" }));
    }

    [Fact]
    public void What_is_sent_is_the_view_the_inputs_describe()
    {
        _queue.Add("crate", "Crate", 2);
        _targets.Set(new KeepTarget(Bench.Rope, "rope", "Rope", 5));

        var marks = new Dictionary<string, string> { ["crate"] = "Makes first: Plank x2" };
        var stamina = StaminaGist.Of(new StaminaRead(3, 8), hasQueue: true);
        var expected = HubView.Build(marks, _queue.Entries, false, "Ready", new KeepView(true, _targets.All, new Dictionary<string, int>()), activeOrderId: _queue.Entries[0].Id,
            stamina: StaminaView.From(new StaminaRead(3, 8), new[] { "plank", "rope" }, hasQueue: true), dyes: Array.Empty<DyeOption>());

        Assert.Equal(expected, Next("Ready", stamina, marks));
    }
}

public class CacheScopeTests
{
    [Fact]
    public void The_first_language_and_save_are_not_a_move()
    {
        Assert.False(new CacheScope().Follow(false, "slot1"));
    }

    [Fact]
    public void The_same_language_and_save_are_not_a_move()
    {
        var scope = new CacheScope();

        scope.Follow(false, "slot1");

        Assert.False(scope.Follow(false, "slot1"));
    }

    [Fact]
    public void A_language_change_is_a_move_once()
    {
        var scope = new CacheScope();

        scope.Follow(false, "slot1");

        Assert.True(scope.Follow(true, "slot1"));
        Assert.False(scope.Follow(true, "slot1"));
    }

    [Fact]
    public void Loading_another_save_is_a_move_once()
    {
        var scope = new CacheScope();

        scope.Follow(false, "slot1");

        Assert.True(scope.Follow(false, "slot2"));
        Assert.False(scope.Follow(false, "slot2"));
    }

    [Fact]
    public void A_save_name_missing_for_a_moment_is_not_a_move_and_the_same_save_after_it_is_not_either()
    {
        var scope = new CacheScope();

        scope.Follow(false, "slot1");

        Assert.False(scope.Follow(false, null));
        Assert.False(scope.Follow(false, "slot1"));
        Assert.True(scope.Follow(false, "slot2"));
    }
}

public class TargetFileTests
{
    [Fact]
    public void Targets_read_back_as_written_even_with_separators_in_names()
    {
        var targets = new KeepTargets(new[] { new KeepTarget(Bench.Rope, "101|202\t3", "Rope\nlong", 5), new KeepTarget(Bench.Plank, "plank", "Plank", 12) });

        var read = TargetFile.Read(TargetFile.Write(null, "Save_2026_10_12.bytes", targets), "Save_2026_10_12.bytes");

        Assert.Equal(targets.All, read.All);
    }

    [Fact]
    public void Writing_one_save_keeps_every_other_save()
    {
        var first = TargetFile.Write(null, "A.bytes", new KeepTargets(new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) }));
        var both = TargetFile.Write(first, "B.bytes", new KeepTargets(new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 9) }));

        var rewritten = TargetFile.Write(both, "A.bytes", new KeepTargets(new[] { new KeepTarget(Bench.Crate, "crate", "Crate", 2) }));

        Assert.Equal(new[] { new KeepTarget(Bench.Crate, "crate", "Crate", 2) }, TargetFile.Read(rewritten, "A.bytes").All);
        Assert.Equal(new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 9) }, TargetFile.Read(rewritten, "B.bytes").All);
    }

    [Fact]
    public void A_line_that_cannot_be_read_is_skipped_but_kept()
    {
        var text = "not a target\r\n" + TargetFile.Write(null, "A.bytes", new KeepTargets(new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) }));

        var rewritten = TargetFile.Write(text, "A.bytes", new KeepTargets());

        Assert.Equal(new[] { new KeepTarget(Bench.Rope, "rope", "Rope", 5) }, TargetFile.Read(text, "A.bytes").All);
        Assert.Contains("not a target", rewritten);
        Assert.Empty(TargetFile.Read(rewritten, "A.bytes").All);
    }

    [Fact]
    public void A_zero_count_line_is_dropped_when_its_save_is_written()
    {
        var text = "A.bytes\t4\t0\trope\tRope\nB.bytes\t4\t0\trope\tRope\n";

        var rewritten = TargetFile.Write(text, "A.bytes", new KeepTargets(new[] { new KeepTarget(Bench.Plank, "plank", "Plank", 9) }));

        Assert.DoesNotContain("A.bytes\t4\t0", rewritten);
        Assert.Contains("B.bytes\t4\t0", rewritten);
    }
}

public class KeepSlotTests
{
    private static readonly KeepTarget Rope = new(Bench.Rope, "rope", "Rope", 5);
    private static readonly KeepTarget Plank = new(Bench.Plank, "plank", "Plank", 9);

    [Fact]
    public void An_unreadable_targets_file_blocks_changes_until_it_reads()
    {
        var store = new FakeStore(failingReads: 1);
        store.Saved["A.bytes"] = new KeepTargets(new[] { Rope });
        var slot = new KeepSlot(store);

        slot.Follow("A.bytes");

        Assert.Equal(SlotLoad.Unreadable, slot.Load());
        Assert.Equal(KeepEdit.Unreadable, slot.Set(Plank));
        Assert.Equal(new[] { Rope }, store.Saved["A.bytes"].All);

        Assert.Equal(SlotLoad.Read, slot.Load());
        Assert.Equal(KeepEdit.Saved, slot.Set(Plank));
        Assert.Equal(new[] { Plank, Rope }, store.Saved["A.bytes"].All.OrderBy(target => target.ItemId));
    }

    [Fact]
    public void Loading_another_save_empties_the_targets_and_says_so()
    {
        var store = new FakeStore(failingReads: 0);
        store.Saved["A.bytes"] = new KeepTargets(new[] { Rope });
        var slot = new KeepSlot(store);

        var first = slot.Follow("A.bytes");
        slot.Load();
        var moved = slot.Follow("B.bytes");

        Assert.False(first);
        Assert.True(moved);
        Assert.Empty(slot.Targets.All);
    }

    [Fact]
    public void A_moment_with_no_save_name_between_reads_of_the_same_save_does_not_count_as_another_save()
    {
        var store = new FakeStore(failingReads: 0);
        store.Saved["A.bytes"] = new KeepTargets(new[] { Rope });
        var slot = new KeepSlot(store);

        slot.Follow("A.bytes");
        slot.Load();
        var lost = slot.Follow(null);
        var back = slot.Follow("A.bytes");
        var load = slot.Load();

        Assert.False(lost);
        Assert.False(back);
        Assert.Equal(SlotLoad.Read, load);
        Assert.Equal(new[] { Rope }, slot.Targets.All);
    }

    [Fact]
    public void Another_save_after_a_moment_with_no_save_name_still_counts_as_another_save()
    {
        var slot = new KeepSlot(new FakeStore(failingReads: 0));

        slot.Follow("A.bytes");
        slot.Follow(null);
        var moved = slot.Follow("B.bytes");

        Assert.True(moved);
    }

    [Fact]
    public void With_no_save_loaded_nothing_is_kept()
    {
        var slot = new KeepSlot(new FakeStore(failingReads: 0));

        Assert.Equal(KeepEdit.NoSave, slot.Set(Rope));
    }

    /// <summary>Keeps targets by save in memory; the first reads can be made to fail like a locked file.</summary>
    private sealed class FakeStore : ITargetStore
    {
        private int _failingReads;

        public FakeStore(int failingReads) => _failingReads = failingReads;

        public Dictionary<string, KeepTargets> Saved { get; } = new();

        public TargetLoad Load(string save)
        {
            if (_failingReads > 0)
            {
                _failingReads--;

                return new TargetLoad.Unreadable();
            }

            return new TargetLoad.Loaded(new KeepTargets(Saved.TryGetValue(save, out var targets) ? targets.All : Array.Empty<KeepTarget>()));
        }

        public bool Save(string save, KeepTargets targets)
        {
            Saved[save] = new KeepTargets(targets.All);

            return true;
        }
    }
}
