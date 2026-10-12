using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>Queue <see cref="Crafts"/> of <see cref="Recipe"/> to bring the target's item back up.</summary>
public readonly record struct TopUpOrder(Recipe Recipe, int Crafts);

/// <summary>The orders to queue, and the names of targets that are low but whose materials are not in the house.</summary>
public sealed record TopUpPlan(IReadOnlyList<TopUpOrder> Orders, IReadOnlyList<string> Short)
{
    public bool IsEmpty => Orders.Count == 0 && Short.Count == 0;
}

/// <summary>
/// Works out what to queue so each keep-stocked target is back at its count. What the house holds and what the queue
/// will already make both count against the target, so checking twice never queues twice. Orders are capped by the
/// materials on hand, including what the queue will already use up, so nothing is queued that would stop the run for
/// want of materials; a target with no materials at all is only reported. Orders plan from the spare stock only, the
/// same way <see cref="QueuePlanner"/> runs them, so no order eats an item another target keeps. Crafts are whole,
/// so a recipe that makes several per craft can end a little over the target, by less than one craft's worth.
/// </summary>
public static class TopUpPlanner
{
    /// <param name="stock">Item id to count of the materials the bench crafts from.</param>
    /// <param name="held">Item id to count of each target's item in the home. A target missing here is skipped.</param>
    public static TopUpPlan Plan(IReadOnlyList<KeepTarget> targets, RecipeBook book, IReadOnlyDictionary<int, int> stock,
        IReadOnlyDictionary<int, int> held, IReadOnlyList<QueueEntry> queue, int maxDepth)
    {
        var left = stock.ToDictionary(pair => pair.Key, pair => pair.Value);
        var orders = new List<TopUpOrder>();
        var shortOf = new List<string>();

        foreach (var entry in queue)
        {
            if (book.Find(entry.Key) is { IsLocked: false } recipe) left = Reserve(book, left, targets, recipe, entry.Crafts, maxDepth, entry.IsTopUp).Left;
        }

        foreach (var target in targets)
        {
            if (!held.TryGetValue(target.ItemId, out var have) || Maker(book, target) is not { } recipe) continue;

            var deficit = target.Count - have - Queued(book, queue, target.ItemId);

            if (deficit <= 0) continue;

            var wanted = (deficit + recipe.Yield - 1) / recipe.Yield;
            var (crafts, after) = Reserve(book, left, targets, recipe, wanted, maxDepth, isTopUp: true);

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
    public static string Describe(TopUpPlan plan)
    {
        var queued = string.Join(", ", plan.Orders.Select(order => $"{order.Recipe.Name} \u00d7{order.Crafts}"));
        var shortOf = string.Join(", ", plan.Short);

        return (queued.Length, shortOf.Length) switch
        {
            (0, 0) => "",
            (_, 0) => "Keep stocked: queued " + queued,
            (0, _) => "Keep stocked: not enough materials for " + shortOf,
            _ => $"Keep stocked: queued {queued}; not enough materials for {shortOf}",
        };
    }

    /// <summary>The recipe the target was set on, while this bench lists it, it is unlocked, and it still makes the item.</summary>
    private static Recipe? Maker(RecipeBook book, KeepTarget target) =>
        book.Find(target.RecipeKey) is { IsLocked: false, Yield: > 0 } recipe && recipe.ProductId == target.ItemId ? recipe : null;

    private static int Queued(RecipeBook book, IReadOnlyList<QueueEntry> queue, int itemId) =>
        queue.Sum(entry => book.Find(entry.Key) is { } recipe && recipe.ProductId == itemId ? entry.Crafts * recipe.Yield : 0);

    /// <summary>
    /// The most crafts, up to <paramref name="wanted"/>, that the stock can make, and the whole stock once they ran. A
    /// top-up plans from the spare stock only, so the kept counts, including what this order just made, stay untouched.
    /// </summary>
    private static (int Crafts, Dictionary<int, int> Left) Reserve(RecipeBook book, Dictionary<int, int> stock, IReadOnlyList<KeepTarget> kept,
        Recipe recipe, int wanted, int maxDepth, bool isTopUp)
    {
        var usable = isTopUp ? KeptStock.Spare(stock, kept) : stock;
        var best = 0;
        IReadOnlyList<CraftStep> bestSteps = Array.Empty<CraftStep>();
        var low = 1;
        var high = wanted;

        // More crafts never need fewer materials, so the plans that work are exactly the counts up to some limit.
        while (low <= high)
        {
            var mid = low + (high - low) / 2;

            if (CraftPlanner.Plan(book, usable, recipe, mid, maxDepth) is CraftPlan.Ready ready)
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
