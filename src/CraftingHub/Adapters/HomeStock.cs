using System;
using System.Collections.Generic;
using GameCore.HotUpdate.Battle.Logic;

namespace CraftingHub.Adapters;

/// <summary>
/// How many of an item the home holds, counted the way the game counts its crafting pool: ItemManager's
/// GetAvailableItemCount sums the player, every home storage (visible or not) and the home's synthetic owners.
/// </summary>
internal sealed class HomeStock
{
    private readonly Action<string> _log;
    private bool _hasLoggedFailure;

    public HomeStock(Action<string> log)
    {
        _log = log;
    }

    /// <summary>Item id to count, or null when the game's item manager cannot be read, so nothing is topped up on a guess.</summary>
    public IReadOnlyDictionary<int, int>? Count(IEnumerable<int> itemIds)
    {
        var items = GameSingleton.Existing<BattleLogicWorld>()?._ItemManager;

        if (items == null) return null;

        try
        {
            var counts = new Dictionary<int, int>();

            foreach (var itemId in itemIds) counts[itemId] = items.GetAvailableItemCount(itemId);

            return counts;
        }
        catch (Exception ex)
        {
            if (!_hasLoggedFailure) _log("could not count the home's items, keep-stocked targets are skipped: " + ex.Message);

            _hasLoggedFailure = true;

            return null;
        }
    }
}
