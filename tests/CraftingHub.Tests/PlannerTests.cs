using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>Keep-stocked planning: what the plan queues is what the run then makes, and kept parts end at their targets.</summary>
public class KeptPartPlanningTests
{
    private const int MaxDepth = 4;

    private static readonly RecipeBook Book = Bench.Book();

    /// <summary>Crate is kept first and needs planks; Plank is kept too, so the crate's top-up has to make its own planks.</summary>
    private static readonly KeepTarget[] CrateThenPlank = { Keep(Bench.Crate, "crate", 1), Keep(Bench.Plank, "plank", 4) };

    [Fact]
    public void A_top_up_that_needs_a_kept_part_refills_it_and_the_run_uses_what_the_plan_said()
    {
        var house = Bench.Mats((Bench.Log, 10), (Bench.Rope, 1), (Bench.Plank, 0));

        var plan = TopUpPlanner.Plan(CrateThenPlank, Book, house, Held(house), Array.Empty<QueueEntry>(), MaxDepth);
        var queue = Queue(plan);
        Run(queue, CrateThenPlank, house);

        Assert.Equal(new[] { ("crate", 1) }, Orders(plan));
        Assert.Empty(plan.Short);
        Assert.Equal((6, 4, 1), (house[Bench.Log], house[Bench.Plank], house[Bench.Crate]));
        Assert.True(TopUpPlanner.Plan(CrateThenPlank, Book, house, Held(house), queue.Entries, MaxDepth).IsEmpty);
    }

    [Fact]
    public void When_the_kept_part_cannot_be_refilled_as_well_the_part_is_topped_up_and_the_rest_is_reported()
    {
        var house = Bench.Mats((Bench.Log, 2), (Bench.Rope, 1), (Bench.Plank, 0));

        var plan = TopUpPlanner.Plan(CrateThenPlank, Book, house, Held(house), Array.Empty<QueueEntry>(), MaxDepth);
        Run(Queue(plan), CrateThenPlank, house);

        Assert.Equal(new[] { ("plank", 2) }, Orders(plan));
        Assert.Equal(new[] { "Crate" }, plan.Short);
        Assert.Equal((0, 4), (house[Bench.Log], house[Bench.Plank]));
    }

    [Fact]
    public void A_half_refilled_kept_part_is_brought_to_its_target_before_any_of_it_is_used()
    {
        // The run replans after every step, so a refill it already started must not be spent on the crate.
        var house = Bench.Mats((Bench.Log, 10), (Bench.Rope, 1), (Bench.Plank, 2));
        var queue = new CraftQueue();
        queue.Add("crate", "Crate", 1, isTopUp: true);

        var next = QueuePlanner.Next(queue.Head, Book, house, CrateThenPlank, MaxDepth, Bench.Name);

        var craft = Assert.IsType<NextStep.Craft>(next);
        Assert.Equal(new CraftStep(Bench.PlankRecipe, 3), craft.Step);
    }

    [Fact]
    public void The_forecast_of_a_top_up_counts_the_refill_of_a_kept_part()
    {
        var house = Bench.Mats((Bench.Log, 10), (Bench.Rope, 1), (Bench.Plank, 0));
        var queue = new[] { new QueueEntry("crate", "Crate", 1, IsTopUp: true, Id: 1) };

        var outlook = QueueForecast.Plan(queue, Book, house, CrateThenPlank, MaxDepth, Bench.Name);

        Assert.Equal("Makes first: Plank x4", outlook.Orders[0].Note);
        Assert.Contains(new MaterialTotal(Bench.Log, "Log", 4, 10, 0), outlook.Totals);
    }

    [Fact]
    public void What_a_queued_order_uses_up_of_a_kept_item_is_topped_up_again()
    {
        var targets = new[] { Keep(Bench.Plank, "plank", 4) };
        var house = Bench.Mats((Bench.Plank, 4), (Bench.Log, 2), (Bench.Rope, 1));
        var queue = new CraftQueue();
        queue.Add("crate", "Crate", 1);

        var plan = TopUpPlanner.Plan(targets, Book, house, Held(house), queue.Entries, MaxDepth);
        foreach (var order in plan.Orders) queue.Add(order.Recipe.Key, order.Recipe.Name, order.Crafts, isTopUp: true);
        Run(queue, targets, house);

        Assert.Equal(new[] { ("plank", 2) }, Orders(plan));
        Assert.Equal((4, 1), (house[Bench.Plank], house[Bench.Crate]));
    }

    [Fact]
    public void Nothing_is_topped_up_while_the_queue_holds_an_order_this_bench_cannot_make_and_the_player_is_told_why()
    {
        // The run stops at such an order, so a top-up behind it would never run here, and what it makes is unknown.
        // Going quiet would read as keep stocked being broken, so the plan carries the reason.
        var targets = new[] { Keep(Bench.Rope, "rope", 5) };
        var stock = Bench.Mats((Bench.Fibre, 30));
        var held = Bench.Mats((Bench.Rope, 0));
        var lockedBook = Bench.Book(Bench.RopeRecipe with { Key = "rope-locked", IsLocked = true });

        var unlisted = TopUpPlanner.Plan(targets, Book, stock, held, new[] { new QueueEntry("saw", "Saw", 1, Id: 1) }, MaxDepth);
        var locked = TopUpPlanner.Plan(targets, lockedBook, stock, held, new[] { new QueueEntry("rope-locked", "Rope", 1, Id: 1) }, MaxDepth);

        Assert.Empty(unlisted.Orders);
        Assert.Empty(locked.Orders);
        Assert.False(unlisted.IsEmpty);
        Assert.Equal("Keep stocked waits: the queue holds Saw, which this workbench cannot make", TopUpPlanner.Describe(unlisted));
        Assert.Equal("Keep stocked waits: the queue holds Rope, which is not unlocked yet", TopUpPlanner.Describe(locked));
        Assert.Equal("Keep stocked waits: the queue holds Saw, which this workbench cannot make (Rope 0/5)", TopUpPlanner.LogLine(unlisted, targets, held));
    }

    private static KeepTarget Keep(int itemId, string key, int count) => new(itemId, key, itemId == Bench.Crate ? "Crate" : Bench.Name(itemId), count);

    /// <summary>The home holds what the bench sees, for every kept item.</summary>
    private static Dictionary<int, int> Held(Dictionary<int, int> house) =>
        new[] { Bench.Crate, Bench.Plank, Bench.Rope }.ToDictionary(id => id, id => house.GetValueOrDefault(id));

    private static (string, int)[] Orders(TopUpPlan plan) => plan.Orders.Select(order => (order.Recipe.Key, order.Crafts)).ToArray();

    private static CraftQueue Queue(TopUpPlan plan)
    {
        var queue = new CraftQueue();

        foreach (var order in plan.Orders) queue.Add(order.Recipe.Key, order.Recipe.Name, order.Crafts, isTopUp: true);

        return queue;
    }

    /// <summary>Runs the queue the way the game does: one step at a time, replanned from the house as it stands. Every order must finish.</summary>
    private static void Run(CraftQueue queue, KeepTarget[] targets, Dictionary<int, int> house)
    {
        for (var steps = 0; steps < 20; steps++)
        {
            var next = QueuePlanner.Next(queue.Head, Book, house, targets, MaxDepth, Bench.Name);

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

public class CraftFloorTests
{
    [Fact]
    public void Only_what_is_above_the_floor_is_used_and_the_rest_is_made()
    {
        var floor = Bench.Mats((Bench.Plank, 4));

        var walk = CraftPlanner.Walk(Bench.Book(), Bench.Mats((Bench.Plank, 6), (Bench.Log, 1), (Bench.Rope, 1)), Bench.CrateRecipe, 1, maxDepth: 4, floor);

        Assert.Equal(new[] { ("plank", 1), ("crate", 1) }, walk.Steps.Select(step => (step.Recipe.Key, step.Crafts)).ToArray());
        Assert.Empty(walk.Missing);
        Assert.Equal((4, 0), (walk.Left[Bench.Plank], walk.Left[Bench.Log]));
    }

    [Fact]
    public void Without_a_floor_everything_owned_may_be_used()
    {
        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), Bench.CrateRecipe, 1, maxDepth: 4);

        Assert.Equal(new[] { new CraftStep(Bench.CrateRecipe, 1) }, Assert.IsType<CraftPlan.Ready>(plan).Steps);
    }

    [Fact]
    public void A_kept_item_that_cannot_be_made_is_short_by_what_the_order_needs_above_the_floor()
    {
        var floor = Bench.Mats((Bench.Fibre, 3));

        var plan = CraftPlanner.Plan(Bench.Book(), Bench.Mats((Bench.Fibre, 4)), Bench.RopeRecipe, 1, maxDepth: 4, floor);

        Assert.Equal(new[] { (Bench.Fibre, 2) }, Assert.IsType<CraftPlan.Short>(plan).Missing.Select(pair => (pair.Key, pair.Value)));
    }
}

public class HeldCountTests
{
    private const long Player = 10;
    private const long Chest = 20;

    [Fact]
    public void An_unstackable_item_counts_as_one_each_as_the_crafting_pool_counts_it()
    {
        var counts = HeldCount.Tally(new[] { Bench.Crate }, new[] { Player, Chest }, Filed(
            (Player, new HeldStack(Player, Bench.Crate, 0)),
            (Chest, new HeldStack(Chest, Bench.Crate, 0))));

        Assert.Equal(2, counts[Bench.Crate]);
    }

    [Fact]
    public void Stacks_add_their_counts_across_every_owner()
    {
        var counts = HeldCount.Tally(new[] { Bench.Plank }, new[] { Player, Chest }, Filed(
            (Player, new HeldStack(Player, Bench.Plank, 3)),
            (Chest, new HeldStack(Chest, Bench.Plank, 5))));

        Assert.Equal(8, counts[Bench.Plank]);
    }

    [Fact]
    public void A_stale_entry_a_repeated_owner_and_no_owner_are_not_counted()
    {
        var counts = HeldCount.Tally(new[] { Bench.Plank }, new[] { Player, Player, 0 }, Filed(
            (Player, new HeldStack(Player, Bench.Plank, 2)),
            (Player, new HeldStack(Chest, Bench.Plank, 7)),
            (0, new HeldStack(0, Bench.Plank, 9))));

        Assert.Equal(2, counts[Bench.Plank]);
    }

    [Fact]
    public void An_item_asked_for_and_not_held_reads_zero_and_others_are_left_out()
    {
        var counts = HeldCount.Tally(new[] { Bench.Rope }, new[] { Player }, Filed((Player, new HeldStack(Player, Bench.Plank, 2))));

        Assert.Equal(new[] { (Bench.Rope, 0) }, counts.Select(pair => (pair.Key, pair.Value)));
    }

    /// <summary>The item cache: each stack filed under an owner, which may not be the owner the stack names (a stale entry).</summary>
    private static Func<long, IEnumerable<HeldStack>> Filed(params (long Under, HeldStack Stack)[] filed) =>
        owner => filed.Where(entry => entry.Under == owner).Select(entry => entry.Stack);
}

public class TopUpLogTests
{
    private static readonly KeepTarget Rope = new(Bench.Rope, "rope", "Rope", 5);

    [Fact]
    public void The_log_line_shows_what_the_home_held_against_the_target_and_what_was_queued()
    {
        var plan = new TopUpPlan(new[] { new TopUpOrder(Bench.RopeRecipe, 3) }, Array.Empty<string>());

        Assert.Equal("Keep stocked: Rope 2/5 -> x3", TopUpPlanner.LogLine(plan, new[] { Rope }, Bench.Mats((Bench.Rope, 2))));
    }

    [Fact]
    public void A_target_without_materials_says_so_and_a_target_left_alone_is_not_listed()
    {
        var plank = new KeepTarget(Bench.Plank, "plank", "Plank", 4);
        var plan = new TopUpPlan(Array.Empty<TopUpOrder>(), new[] { "Rope" });

        var line = TopUpPlanner.LogLine(plan, new[] { Rope, plank }, Bench.Mats((Bench.Rope, 0), (Bench.Plank, 4)));

        Assert.Equal("Keep stocked: Rope 0/5 -> not enough materials", line);
    }

    [Fact]
    public void An_empty_plan_logs_nothing()
    {
        var plan = new TopUpPlan(Array.Empty<TopUpOrder>(), Array.Empty<string>());

        Assert.Equal("", TopUpPlanner.LogLine(plan, new[] { Rope }, Bench.Mats((Bench.Rope, 5))));
    }
}

/// <summary>A player's order is queued with the intermediates it needs ahead of it, planned from what the queue leaves.</summary>
public class QueueSplitTests
{
    private const int MaxDepth = 4;

    private static readonly RecipeBook Book = Bench.Book();
    private static readonly NewOrder OneCrate = new("crate", "Crate", 1);

    [Fact]
    public void Intermediates_are_queued_as_their_own_orders_ahead_of_the_order_and_the_queue_makes_it()
    {
        var house = Bench.Mats((Bench.Log, 2), (Bench.Fibre, 3));
        var queue = new CraftQueue();

        Add(queue, house, OneCrate);
        Run(queue, house);

        Assert.Equal(new[] { ("plank", 2), ("rope", 1), ("crate", 1) }, Orders(queue));
        Assert.Equal(1, house[Bench.Crate]);
    }

    [Fact]
    public void Stock_the_orders_already_queued_will_use_is_not_counted_for_the_new_one()
    {
        var house = Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1), (Bench.Log, 2), (Bench.Fibre, 3));
        var queue = new CraftQueue();

        Add(queue, house, OneCrate);
        Add(queue, house, OneCrate);

        Assert.Equal(new[] { ("crate", 1), ("plank", 2), ("rope", 1), ("crate", 1) }, Orders(queue));
    }

    [Fact]
    public void An_order_that_is_short_or_needs_nothing_made_first_is_queued_as_asked()
    {
        var shortQueue = new CraftQueue();
        var readyQueue = new CraftQueue();

        Add(shortQueue, Bench.Mats((Bench.Fibre, 3)), OneCrate);
        Add(readyQueue, Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1)), OneCrate);

        Assert.Equal(new[] { ("crate", 1) }, Orders(shortQueue));
        Assert.Equal(new[] { ("crate", 1) }, Orders(readyQueue));
    }

    private static void Add(CraftQueue queue, Dictionary<int, int> house, NewOrder order)
    {
        foreach (var queued in QueueForecast.Split(queue.Entries, Book, house, Array.Empty<KeepTarget>(), MaxDepth, order)) queue.Add(queued.Key, queued.Name, queued.Crafts);
    }

    private static (string, int)[] Orders(CraftQueue queue) => queue.Entries.Select(entry => (entry.Key, entry.Crafts)).ToArray();

    /// <summary>Runs a copy of the queue to the end as the hub does, so the queue keeps the orders as they were added.</summary>
    private static void Run(CraftQueue queue, Dictionary<int, int> house)
    {
        var running = new CraftQueue();

        foreach (var entry in queue.Entries) running.Add(entry.Key, entry.Name, entry.Crafts, entry.IsTopUp);

        for (var steps = 0; steps < 20; steps++)
        {
            var next = QueuePlanner.Next(running.Head, Book, house, Array.Empty<KeepTarget>(), MaxDepth, Bench.Name);

            if (next is NextStep.Finished) return;

            var craft = Assert.IsType<NextStep.Craft>(next);

            Assert.True(craft.IsTarget, "every intermediate already has its own order");

            foreach (var (itemId, perCraft) in craft.Step.Recipe.Materials) house[itemId] = house.GetValueOrDefault(itemId) - perCraft * craft.Step.Crafts;

            house[craft.Step.Recipe.ProductId] = house.GetValueOrDefault(craft.Step.Recipe.ProductId) + craft.Step.Crafts * craft.Step.Recipe.Yield;
            running.Progress(craft.OrderId, craft.Step.Crafts);
        }

        Assert.Fail("the queue never finished");
    }
}

/// <summary>Colored recipes: filled by the game's own key and planned with their dye like any other material.</summary>
public class ColoredRecipeTests
{
    private const int RedDye = 90;
    private const int RedCrate = 50;

    private static readonly Recipe RedCrateRecipe = new(RecipeBook.KeyOf(new[] { RedDye, Bench.Plank, Bench.Plank, Bench.Plank, Bench.Plank, Bench.Rope }), "Red Crate", RedCrate, 1,
        Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1), (RedDye, 1)), IsLocked: false, BaseKey: "crate", DyeId: RedDye);

    [Fact]
    public void The_key_is_every_material_unit_in_number_order_as_the_game_builds_it()
    {
        Assert.Equal("2,2,9,10", RecipeBook.KeyOf(new[] { 10, 2, 9, 2 }));
        Assert.Equal("2,2,2,2,4,90", RedCrateRecipe.Key);
    }

    [Fact]
    public void A_colored_recipe_without_its_dye_is_short_of_the_dye_alone()
    {
        var book = Bench.Book(RedCrateRecipe);
        var house = Bench.Mats((Bench.Plank, 4), (Bench.Rope, 1));

        var plan = CraftPlanner.Plan(book, house, RedCrateRecipe, 1, maxDepth: 4);

        Assert.Equal(new Dictionary<int, int> { [RedDye] = 1 }, Assert.IsType<CraftPlan.Short>(plan).Missing);
    }

    [Fact]
    public void A_colored_recipe_makes_its_planks_first_like_the_plain_one()
    {
        var book = Bench.Book(RedCrateRecipe);
        var house = Bench.Mats((Bench.Log, 2), (Bench.Rope, 1), (RedDye, 1));

        var plan = Assert.IsType<CraftPlan.Ready>(CraftPlanner.Plan(book, house, RedCrateRecipe, 1, maxDepth: 4));

        Assert.Equal(new[] { ("plank", 2), (RedCrateRecipe.Key, 1) }, plan.Steps.Select(step => (step.Recipe.Key, step.Crafts)));
    }
}
