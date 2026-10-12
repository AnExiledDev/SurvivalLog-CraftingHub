using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>
/// Turns the head of the queue into the next craft to run, from the bench's recipes and the stock right now. A
/// keep-stocked order only uses what is spare above the kept counts, so it makes its intermediates rather than eat
/// another kept item; the player's own orders may use anything in the house.
/// </summary>
public static class QueuePlanner
{
    public static NextStep Next(QueueEntry? head, RecipeBook book, IReadOnlyDictionary<int, int> stock, IReadOnlyList<KeepTarget> kept, int maxDepth, Func<int, string> itemName)
    {
        if (head is not { } order) return new NextStep.Finished();

        if (book.Find(order.Key) is not { } target) return new NextStep.Blocked($"{order.Name} is not in your recipe list yet");

        if (target.IsLocked) return new NextStep.Blocked($"{order.Name} is not unlocked yet");

        var usable = order.IsTopUp ? KeptStock.Spare(stock, kept) : stock;

        return CraftPlanner.Plan(book, usable, target, order.Crafts, maxDepth) switch
        {
            CraftPlan.Ready ready => new NextStep.Craft(ready.Steps[0], IsTarget: ready.Steps.Count == 1, OrderId: order.Id),
            CraftPlan.Short shortOf => new NextStep.Blocked($"can't make {order.Name} ×{order.Crafts}: short of {Missing(shortOf.Missing, itemName)}"),
            _ => new NextStep.Blocked("no plan for " + order.Name),
        };
    }

    public static string Missing(IReadOnlyDictionary<int, int> missing, Func<int, string> itemName) =>
        string.Join(", ", missing.OrderBy(pair => pair.Key).Select(pair => $"{itemName(pair.Key)} ×{pair.Value}"));
}
