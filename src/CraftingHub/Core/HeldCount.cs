using System;
using System.Collections.Generic;

namespace CraftingHub.Core;

/// <summary>One stack in a container's item list: the owner the item says it is in, its item id and its count.</summary>
public readonly record struct HeldStack(long OwnerId, int ItemId, int Count);

/// <summary>
/// Counts the home's items the way the game's crafting pool counts them (Reducer_Web_ToolTable.CountOwnedInPool): an
/// item with a count of zero or less is one unit, since unstackable items carry no count. Each container counts once,
/// and a cached item whose owner has moved on is left out, as the game's own item lists leave it out.
/// </summary>
public static class HeldCount
{
    public static int Units(int count) => count <= 0 ? 1 : count;

    /// <summary>Item id to units held, over every owner, for each of <paramref name="itemIds"/> (0 when none are held).</summary>
    public static Dictionary<int, int> Tally(IEnumerable<int> itemIds, IEnumerable<long> owners, Func<long, IEnumerable<HeldStack>> stacksIn)
    {
        var counts = new Dictionary<int, int>();
        var counted = new HashSet<long>();

        foreach (var itemId in itemIds) counts[itemId] = 0;

        foreach (var owner in owners)
        {
            if (owner == 0 || !counted.Add(owner)) continue;

            foreach (var stack in stacksIn(owner))
            {
                if (stack.OwnerId != owner || !counts.ContainsKey(stack.ItemId)) continue;

                counts[stack.ItemId] += Units(stack.Count);
            }
        }

        return counts;
    }
}
