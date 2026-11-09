using System;
using System.Collections.Generic;
using CraftingHub.Core;
using ExiledToolkit.Game;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using Il2CppOwnerList = Il2CppSystem.Collections.Generic.List<long>;

namespace CraftingHub.Adapters;

/// <summary>
/// Adds every home storage to the workbench's linked storages. The game asks this one list for counting, for the
/// craftable check, for the max-craft count and for laying out materials, so one postfix makes all of them use the
/// whole house. Static because a Harmony postfix has nowhere else to look.
/// </summary>
internal static class StoragePool
{
    private static readonly List<long> HomeCache = new();

    // Reused by every ask; the hook runs on the main thread only.
    private static readonly List<long> Linked = new();
    private static readonly long[] Excluded = new long[2];
    private static readonly HashSet<long> Seen = new();
    private static readonly List<long> Added = new();
    private static int _cachedFrame = -1;
    private static PatchKit? _hooks;
    private static bool _isOn;
    private static bool _isHooked;

    /// <summary>
    /// Called on the main thread at start and after each change. The hook goes in the first time the setting is on and
    /// stays; off, it adds nothing and the game's list is used.
    /// </summary>
    public static void Use(PatchKit hooks, bool isOn, Action<string> log)
    {
        _hooks = hooks;
        _isOn = isOn;

        if (isOn && !_isHooked) Hook(hooks);

        log(isOn ? "workbenches use every home storage" : "workbenches use only their linked storages");
    }

    /// <summary>A hook that failed is not tried again.</summary>
    private static void Hook(PatchKit hooks)
    {
        _isHooked = true;

        hooks.Patch(typeof(StoragePool), typeof(Reducer_Web_ToolTable), nameof(Reducer_Web_ToolTable.GetAllLinkedOwnerIds), postfix: nameof(AddHomeStorages),
            done: "hooked the workbench's storage list", consequence: "only nearby storages will count");
    }

    private static void AddHomeStorages(State_Web_ToolTable state, Il2CppOwnerList __result)
    {
        if (!_isOn || state == null || __result == null) return;

        try
        {
            Linked.Clear();

            for (var i = 0; i < __result.Count; i++) Linked.Add(__result[i]);

            Excluded[0] = state.WorkbenchOwnerId?.Value ?? 0;
            Excluded[1] = state.CurrentFurnitureId;

            foreach (var owner in HomeOwners.ToAdd(Linked, HomeStorages(), Excluded, Seen, Added)) __result.Add(owner);
        }
        catch (Exception ex)
        {
            _hooks?.Fail("adding home storages, the game keeps its own list", ex);
        }
    }

    /// <summary>The game asks for the list many times in one refresh, so it is read once per frame.</summary>
    private static List<long> HomeStorages()
    {
        if (_cachedFrame == UnityFrame.Count()) return HomeCache;

        HomeCache.Clear();
        _cachedFrame = UnityFrame.Count();

        var agents = GameSingleton.Existing<BattleLogicWorld>()?._AgentManager;
        var found = agents?.GetFurnituresWithBag(agents.GetHomeMapId(), true, true);

        for (var i = 0; found != null && i < found.Count; i++)
        {
            var furniture = found[i];

            if (furniture != null && !ItemManager.IsExcludedFromCostPool(furniture.InstanceId) && !IsNeighborBasket(furniture)) HomeCache.Add(furniture.InstanceId);
        }

        return HomeCache;
    }

    /// <summary>
    /// The neighbor's basket holds what the player staged for her, which the game's cost pool does not exclude, so
    /// crafting would use it up. Its config id is 0 when there is no basket.
    /// </summary>
    public static bool IsNeighborBasket(Furniture furniture)
    {
        var basketId = NeighborRules.GetBasketFurnitureId();

        return basketId > 0 && furniture.AgentConfigId == basketId;
    }
}
