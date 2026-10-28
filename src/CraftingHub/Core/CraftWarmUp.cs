using System;
using System.Collections.Generic;

namespace CraftingHub.Core;

/// <summary>What one warm-up pass worked out, so a test can tell it really planned, costed and drew the panel.</summary>
public readonly record struct WarmUpResult(int Marks, OrderState LastOrder, int TopUpOrders, bool HasView, RunnerCommand FirstRunStep);

/// <summary>
/// Does once, on a made-up workbench, the work the first workbench open of a session does: chain marks, the queue's
/// plan and costing, keep-stocked top-ups and the panel. The code it runs, and the dictionaries, sorts and string
/// joins it builds, are then compiled before the player opens a workbench, instead of on the game's frame. Plain .NET
/// only, so it can run on a worker at load.
/// </summary>
public static class CraftWarmUp
{
    private const int MaxDepth = 4;
    private const int Log = 1;
    private const int Plank = 2;
    private const int Fibre = 3;
    private const int Rope = 4;
    private const int Crate = 5;

    public static WarmUpResult Run()
    {
        // A crate needs planks and rope, which are made from logs and fibre, so every plan goes through intermediates.
        var book = new RecipeBook(new[]
        {
            new Recipe("plank", "Plank", Plank, 2, new Dictionary<int, int> { [Log] = 1 }, IsLocked: false),
            new Recipe("rope", "Rope", Rope, 1, new Dictionary<int, int> { [Fibre] = 3 }, IsLocked: false),
            new Recipe("crate", "Crate", Crate, 1, new Dictionary<int, int> { [Plank] = 4, [Rope] = 1 }, IsLocked: false),
        });
        var stock = new Dictionary<int, int> { [Log] = 4, [Fibre] = 6 };
        var targets = new KeepTargets(new[] { new KeepTarget(Crate, "crate", "Crate", 1) });
        var held = new Dictionary<int, int> { [Crate] = 0 };
        var queue = new CraftQueue();
        Func<int, string> itemName = id => "Item " + id;

        var marks = ChainMarks.Find(book, stock, new HashSet<string> { "plank", "rope" }, MaxDepth);
        var topUp = TopUpPlanner.Plan(targets.All, book, stock, held, queue.Entries, MaxDepth);

        TopUpPlanner.Describe(topUp);
        TopUpPlanner.LogLine(topUp, targets.All, held);

        foreach (var order in QueueForecast.Split(queue.Entries, book, stock, targets.All, MaxDepth, new NewOrder("crate", "Crate", 1)))
        {
            queue.Add(order.Key, order.Name, order.Crafts);
        }

        queue.Add("crate", "Crate", 1, isTopUp: true);

        var outlook = QueueForecast.Plan(queue.Entries, book, stock, targets.All, MaxDepth, itemName);
        var next = QueuePlanner.Next(queue.Head, book, stock, targets.All, MaxDepth, itemName);
        var runner = new QueueRunner();

        runner.Start();

        var firstStep = runner.Tick(new BenchView(IsOpen: true, IsBusy: false, HasResult: false, MaxCrafts: 9, stock, BatchDone: 0, BenchId: 1), 0, () => next);
        var keys = new List<string>();

        foreach (var recipe in book.All) keys.Add(recipe.Key);

        PageCommand.Parse(PageCommand.Prefix + "add|crate|2");

        var view = new ViewGate().Next(new ViewInputs(marks, queue, queue.Version, IsRunning: true, IsPaused: false, queue.Head?.Id ?? 0, "", KeepOn: true,
            targets, targets.Version, targets.ByRecipe(book), held, outlook, new Dictionary<string, int> { ["plank"] = 2 }, keys,
            StaminaGist.Of(new StaminaRead(50, 5), hasQueue: true), TakesUnits: false, Array.Empty<DyeOption>()));

        return new WarmUpResult(marks.Count, outlook.Orders.Count > 0 ? outlook.Orders[^1].State : OrderState.NotListed, topUp.Orders.Count, view != null, firstStep);
    }
}
