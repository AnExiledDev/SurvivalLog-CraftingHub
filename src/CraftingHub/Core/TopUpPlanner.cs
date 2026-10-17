using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>Queue <see cref="Crafts"/> of <see cref="Recipe"/> to bring the target's item back up.</summary>
public readonly record struct TopUpOrder(Recipe Recipe, int Crafts);

/// <summary>
/// The orders to queue, and the names of targets that are low but whose materials are not in the house. <see cref="Waiting"/>
/// says why nothing was planned at all, or is "" when the targets were checked.
/// </summary>
public sealed record TopUpPlan(IReadOnlyList<TopUpOrder> Orders, IReadOnlyList<string> Short, string Waiting = "")
{
    public bool IsEmpty => Orders.Count == 0 && Short.Count == 0 && Waiting.Length == 0;
}

/// <summary>
/// Works out what to queue so each keep-stocked target is back at its count. What the house holds, plus what the queue
/// will make and less what it will use up, counts against the target, so checking twice never queues twice. Orders are
/// capped by the materials on hand, including what the queue will already use up, so nothing is queued that would stop
/// the run for want of materials; a target with no materials at all is only reported. Orders plan with the kept counts
/// as their floor, the same way <see cref="QueuePlanner"/> runs them, so no order eats an item another target keeps,
/// and a kept part an order has to make is refilled by it. Crafts are whole, so a recipe that makes several per craft
/// can end a little over the target, by less than one craft's worth.
/// </summary>
public static class TopUpPlanner
{
    /// <param name="stock">Item id to count of the materials the bench crafts from.</param>
    /// <param name="held">Item id to count of each target's item in the home. A target missing here is skipped.</param>
    public static TopUpPlan Plan(IReadOnlyList<KeepTarget> targets, RecipeBook book, IReadOnlyDictionary<int, int> stock,
        IReadOnlyDictionary<int, int> held, IReadOnlyList<QueueEntry> queue, int maxDepth, bool isChinese = false)
    {
        var floor = KeptStock.Floor(targets);
        var left = stock.ToDictionary(pair => pair.Key, pair => pair.Value);
        var orders = new List<TopUpOrder>();
        var shortOf = new List<string>();

        foreach (var entry in queue)
        {
            // The run stops at an order this bench cannot make, so nothing queued behind it would run from here, and
            // what that order makes and uses cannot be known; topping up now would only guess.
            if (book.Find(entry.Key) is not { } recipe) return Waits(Texts.WaitsCannotMake.Say(isChinese, entry.Name));

            if (recipe.IsLocked) return Waits(Texts.WaitsLocked.Say(isChinese, entry.Name));

            left = new Dictionary<int, int>(CraftPlanner.Walk(book, left, recipe, entry.Crafts, maxDepth, entry.IsTopUp ? floor : null).Left);
        }

        foreach (var target in targets)
        {
            if (!held.TryGetValue(target.ItemId, out var have) || Maker(book, target) is not { } recipe) continue;

            // The queue and the orders planned so far change the count by what they leave against what is there now.
            var deficit = target.Count - have - (left.GetValueOrDefault(target.ItemId) - stock.GetValueOrDefault(target.ItemId));

            if (deficit <= 0) continue;

            var wanted = (deficit + recipe.Yield - 1) / recipe.Yield;
            var (crafts, after) = Reserve(book, left, floor, recipe, wanted, maxDepth);

            if (crafts == 0)
            {
                shortOf.Add(target.Name);

                continue;
            }

            left = after;
            orders.Add(new TopUpOrder(recipe, crafts));
        }

        return new TopUpPlan(orders, shortOf);
    }

    /// <summary>The status line for a plan, or "" when there is nothing to say.</summary>
    public static string Describe(TopUpPlan plan, bool isChinese = false)
    {
        if (plan.Waiting.Length > 0) return Texts.KeepWaits.Say(isChinese, plan.Waiting);

        var queued = Texts.Join(isChinese, plan.Orders.Select(order => $"{order.Recipe.Name} \u00d7{order.Crafts}"));
        var shortOf = Texts.Join(isChinese, plan.Short);

        return (queued.Length, shortOf.Length) switch
        {
            (0, 0) => "",
            (_, 0) => Texts.KeepQueued.Say(isChinese, queued),
            (0, _) => Texts.KeepShort.Say(isChinese, shortOf),
            _ => Texts.KeepQueuedAndShort.Say(isChinese, queued, shortOf),
        };
    }

    /// <summary>
    /// The log line for a plan: each target it acts on, with what the home held against the target and what was done,
    /// so a count that reads wrong shows up in the log; while the plan waits, why, with every target's count. "" when
    /// there is nothing to say.
    /// </summary>
    public static string LogLine(TopUpPlan plan, IReadOnlyList<KeepTarget> targets, IReadOnlyDictionary<int, int> held)
    {
        string Counted(KeepTarget target) => $"{target.Name} {held.GetValueOrDefault(target.ItemId)}/{target.Count}";

        if (plan.Waiting.Length > 0) return $"{WaitsPrefix}{plan.Waiting} ({string.Join(", ", targets.Select(Counted))})";

        var lines = new List<string>();

        foreach (var target in targets)
        {
            var counted = Counted(target);

            if (plan.Orders.FirstOrDefault(candidate => candidate.Recipe.Key == target.RecipeKey) is { Crafts: > 0 } order) lines.Add($"{counted} -> x{order.Crafts}");
            else if (plan.Short.Contains(target.Name)) lines.Add($"{counted} -> not enough materials");
        }

        return lines.Count == 0 ? "" : "Keep stocked: " + string.Join(", ", lines);
    }

    private const string WaitsPrefix = "Keep stocked waits: ";

    /// <summary>Nothing planned, and why.</summary>
    private static TopUpPlan Waits(string why) => new(Array.Empty<TopUpOrder>(), Array.Empty<string>(), why);

    /// <summary>The recipe the target was set on, while this bench lists it, it is unlocked, and it still makes the item.</summary>
    private static Recipe? Maker(RecipeBook book, KeepTarget target) =>
        book.Find(target.RecipeKey) is { IsLocked: false, Yield: > 0 } recipe && recipe.ProductId == target.ItemId ? recipe : null;

    /// <summary>
    /// The most crafts, up to <paramref name="wanted"/>, that the stock can make, and the whole stock once they ran. A
    /// top-up plans above the kept counts only, so they stay untouched, or are refilled when the order makes that item.
    /// </summary>
    private static (int Crafts, Dictionary<int, int> Left) Reserve(RecipeBook book, Dictionary<int, int> stock, IReadOnlyDictionary<int, int> floor,
        Recipe recipe, int wanted, int maxDepth)
    {
        var best = 0;
        IReadOnlyList<CraftStep> bestSteps = Array.Empty<CraftStep>();
        var low = 1;
        var high = wanted;

        // More crafts never need fewer materials, so the plans that work are exactly the counts up to some limit.
        while (low <= high)
        {
            var mid = low + (high - low) / 2;

            if (CraftPlanner.Plan(book, stock, recipe, mid, maxDepth, floor) is CraftPlan.Ready ready)
            {
                best = mid;
                bestSteps = ready.Steps;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return (best, After(stock, bestSteps));
    }

    /// <summary>The stock once the steps ran: their materials used up and their products added.</summary>
    private static Dictionary<int, int> After(Dictionary<int, int> stock, IReadOnlyList<CraftStep> steps)
    {
        var left = new Dictionary<int, int>(stock);

        foreach (var step in steps)
        {
            foreach (var (itemId, perCraft) in step.Recipe.Materials) left[itemId] = Math.Max(0, left.GetValueOrDefault(itemId) - perCraft * step.Crafts);

            left[step.Recipe.ProductId] = left.GetValueOrDefault(step.Recipe.ProductId) + step.Crafts * step.Recipe.Yield;
        }

        return left;
    }
}
