using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>
/// Turns the head of the queue into the next craft to run, from the bench's recipes and the stock right now. A
/// keep-stocked order only uses what is spare above the kept counts, so it makes its intermediates rather than eat
/// another kept item, and refills a kept item it has to make; the player's own orders may use anything in the house.
/// </summary>
public static class QueuePlanner
{
    public static NextStep Next(QueueEntry? head, RecipeBook book, IReadOnlyDictionary<int, int> stock, IReadOnlyList<KeepTarget> kept, int maxDepth, Func<int, string> itemName, bool isChinese = false)
    {
        if (head is not { } order) return new NextStep.Finished();

        if (book.Find(order.Key) is not { } target) return new NextStep.Blocked(Texts.NotListed.Say(isChinese, order.Name));

        if (target.IsLocked) return new NextStep.Blocked(Texts.NotUnlocked.Say(isChinese, order.Name));

        var floor = order.IsTopUp ? KeptStock.Floor(kept) : null;

        return CraftPlanner.Plan(book, stock, target, order.Crafts, maxDepth, floor) switch
        {
            CraftPlan.Ready ready => new NextStep.Craft(ready.Steps[0], IsTarget: ready.Steps.Count == 1, OrderId: order.Id),
            CraftPlan.Short shortOf => new NextStep.Blocked(Texts.CannotMake.Say(isChinese, order.Name, order.Crafts, Missing(shortOf.Missing, itemName, isChinese))),
            _ => new NextStep.Blocked(Texts.NoPlan.Say(isChinese, order.Name)),
        };
    }

    public static string Missing(IReadOnlyDictionary<int, int> missing, Func<int, string> itemName, bool isChinese = false) =>
        Texts.Join(isChinese, missing.OrderBy(pair => pair.Key).Select(pair => $"{itemName(pair.Key)} ×{pair.Value}"));
}
