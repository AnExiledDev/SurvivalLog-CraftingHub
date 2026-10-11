using System;
using System.Collections.Generic;
using CraftingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;
using UnityEngine;
using Il2CppOwnerList = Il2CppSystem.Collections.Generic.List<long>;

namespace CraftingHub.Adapters;

/// <summary>
/// Adds every home storage to the workbench's linked storages. The game asks this one list for counting, for the
/// craftable check, for the max-craft count and for laying out materials, so one postfix makes all of them use the
/// whole house. Static because a Harmony postfix has nowhere else to look.
/// </summary>
internal static class StoragePool
{
    private const int MaxLoggedFailures = 3;

    private static readonly List<long> HomeCache = new();
    private static int _cachedFrame = -1;
    private static Action<string> _log = _ => { };
    private static int _failures;

    public static void Apply(Harmony harmony, Action<string> log)
    {
        _log = log;

        try
        {
            harmony.Patch(AccessTools.Method(typeof(Reducer_Web_ToolTable), nameof(Reducer_Web_ToolTable.GetAllLinkedOwnerIds)),
                postfix: new HarmonyMethod(typeof(StoragePool), nameof(AddHomeStorages)));
            log("workbenches use every home storage");
        }
        catch (Exception ex)
        {
            log("could not hook the workbench's storage list, only nearby storages will count: " + ex.Message);
        }
    }

    private static void AddHomeStorages(State_Web_ToolTable state, Il2CppOwnerList __result)
    {
        if (state == null || __result == null) return;

        try
        {
            var linked = new List<long>(__result.Count);

            for (var i = 0; i < __result.Count; i++) linked.Add(__result[i]);

            var excluded = new[] { state.WorkbenchOwnerId?.Value ?? 0, state.CurrentFurnitureId };

            foreach (var owner in HomeOwners.ToAdd(linked, HomeStorages(), excluded)) __result.Add(owner);
        }
        catch (Exception ex)
        {
            _failures++;

            if (_failures <= MaxLoggedFailures) _log($"adding home storages failed, the game keeps its own list (logged {_failures} of {MaxLoggedFailures}): {ex}");
        }
    }

    /// <summary>The game asks for the list many times in one refresh, so it is read once per frame.</summary>
    private static List<long> HomeStorages()
    {
        if (_cachedFrame == Time.frameCount) return HomeCache;

        HomeCache.Clear();
        _cachedFrame = Time.frameCount;

        var agents = GameSingleton.Existing<BattleLogicWorld>()?._AgentManager;
        var found = agents?.GetFurnituresWithBag(agents.GetHomeMapId(), true, true);

        for (var i = 0; found != null && i < found.Count; i++)
        {
            var furniture = found[i];

            if (furniture != null && !ItemManager.IsExcludedFromCostPool(furniture.InstanceId)) HomeCache.Add(furniture.InstanceId);
        }

        return HomeCache;
    }
}
