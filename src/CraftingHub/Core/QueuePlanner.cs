using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>Turns the head of the queue into the next craft to run, from the bench's recipes and the stock right now.</summary>
public static class QueuePlanner
{
    public static NextStep Next(QueueEntry? head, RecipeBook book, IReadOnlyDictionary<int, int> stock, int maxDepth, Func<int, string> itemName)
    {
        if (head is not { } order) return new NextStep.Finished();

        if (book.Find(order.Key) is not { } target) return new NextStep.Blocked($"{order.Name} is not made at this workbench");

        if (target.IsLocked) return new NextStep.Blocked($"{order.Name} is not unlocked yet");

        return CraftPlanner.Plan(book, stock, target, order.Crafts, maxDepth) switch
        {
            CraftPlan.Ready ready => new NextStep.Craft(ready.Steps[0], IsTarget: ready.Steps.Count == 1),
            CraftPlan.Short shortOf => new NextStep.Blocked($"can't make {order.Name} ×{order.Crafts}: short of {Missing(shortOf.Missing, itemName)}"),
            _ => new NextStep.Blocked("no plan for " + order.Name),
        };
    }

    public static string Missing(IReadOnlyDictionary<int, int> missing, Func<int, string> itemName) =>
        string.Join(", ", missing.OrderBy(pair => pair.Key).Select(pair => $"{itemName(pair.Key)} ×{pair.Value}"));
}
