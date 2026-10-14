using System;
using System.Collections.Generic;
using CraftingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using Il2CppInterop.Runtime;
using Il2CppItems = Il2CppSystem.Collections.Generic.Dictionary<long, Il2CppSystem.Collections.Generic.Dictionary<long, GameCore.HotUpdate.ReduxUI.Data_Item>>;

namespace CraftingHub.Adapters;

/// <summary>
/// How many of an item the home holds, over the owners ItemManager.GetAvailableItemCount scans (the player, every home
/// storage visible or not, and the workbench drawer, its one synthetic owner). That method sums raw counts, so an
/// unstackable item, which carries a count of 0, read as none; this counts units the way the crafting pool does.
/// </summary>
internal sealed class HomeStock
{
    private readonly Action<string> _log;
    private bool _hasLoggedFailure;

    public HomeStock(Action<string> log)
    {
        _log = log;
    }

    /// <summary>Item id to count, or null when the game's items cannot be read, so nothing is topped up on a guess.</summary>
    public IReadOnlyDictionary<int, int>? Count(IEnumerable<int> itemIds)
    {
        var agents = GameSingleton.Existing<BattleLogicWorld>()?._AgentManager;
        var cache = GameSingleton.Existing<ReduxUISystem>()?.GetData<State_Data_Item>(Il2CppType.Of<State_Data_Item>())?.OwnerCache;

        if (agents == null || cache == null) return null;

        try
        {
            return HeldCount.Tally(itemIds, Owners(agents), owner => StacksIn(cache, owner));
        }
        catch (Exception ex)
        {
            if (!_hasLoggedFailure) _log("could not count the home's items, keep-stocked targets are skipped: " + ex.Message);

            _hasLoggedFailure = true;

            return null;
        }
    }

    /// <summary>The same owners, and the same storage query, as GetAvailableItemCount (rva 0x2581980).</summary>
    private static List<long> Owners(AgentManager agents)
    {
        var owners = new List<long> { agents.LeadingRoleId, WorkbenchDrawerConfig.DrawerOwnerId };
        var storages = agents.GetFurnituresWithBag(agents.GetHomeMapId(), false, true, false);

        for (var i = 0; storages != null && i < storages.Count; i++)
        {
            var furniture = storages[i];

            if (furniture != null) owners.Add(furniture.InstanceId);
        }

        return owners;
    }

    private static IEnumerable<HeldStack> StacksIn(Il2CppItems cache, long owner)
    {
        if (!cache.TryGetValue(owner, out var held) || held == null) yield break;

        foreach (var item in held.Values)
        {
            if (item != null) yield return new HeldStack(item.OwnerId, item.ItemConfigId, item.ItemCount);
        }
    }
}
