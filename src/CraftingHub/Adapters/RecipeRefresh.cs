using System;
using System.Collections.Generic;
using CraftingHub.Core;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;
using Il2CppNeeded = Il2CppSystem.Collections.Generic.Dictionary<int, int>;

namespace CraftingHub.Adapters;

/// <summary>
/// Keeps the workbench responsive while crafting. The game rebuilds its whole recipe list after every item it moves, and
/// each rebuild checks every recipe against every storage, so pulling materials or collecting a craft froze the game
/// for seconds. Rebuilds are held and run once (<see cref="RefreshBatch{T}"/>), before anything reads the list and at
/// the end of the frame, and while one runs the craftable test is answered from one look at the storages
/// (<see cref="MaterialPool"/>). Static because Harmony patches have nowhere else to look.
/// </summary>
internal static class RecipeRefresh
{
    /// <summary>The fast craftable test is checked against the game's own this many times before it is trusted alone.</summary>
    private const int ChecksCompared = 400;

    private const int MaxLoggedFailures = 3;

    private sealed record Rebuild(State_Web_ToolTable State, State_Data_Furniture Furniture, State_Data_Item Items);

    private static readonly RefreshBatch<Rebuild> Batch = new();
    private static Action<string> _log = _ => { };
    private static int _rebuildsRunning;
    private static MaterialPool? _pool;
    private static bool _isPoolTrusted = true;
    private static bool _isAskingGame;
    private static int _compared;
    private static int _failures;

    public static void Apply(Harmony harmony, Action<string> log)
    {
        _log = log;

        // The steps that read the list go first: a rebuild is only ever held once all of them run it beforehand.
        try
        {
            Patch(harmony, nameof(Reducer_Web_ToolTable.RA_Make), prefix: nameof(FlushFirst));
            Patch(harmony, nameof(Reducer_Web_ToolTable.RA_RecipeClick), prefix: nameof(FlushFirst));
            Patch(harmony, nameof(Reducer_Web_ToolTable.EvalCanCraftAgain), prefix: nameof(FlushFirst));
            Patch(harmony, nameof(Reducer_Web_ToolTable.RA_Close), prefix: nameof(DropHeld));
            Patch(harmony, nameof(Reducer_Web_ToolTable.RefreshRecipeList), prefix: nameof(RunOrHold), postfix: nameof(Rebuilt));
            log("workbench recipe list rebuilds are batched");
        }
        catch (Exception ex)
        {
            log("could not batch the workbench recipe list rebuilds, the game rebuilds it as before: " + ex.Message);
        }

        try
        {
            Patch(harmony, nameof(Reducer_Web_ToolTable.CheckMaterialSufficiencyAcrossLinked), prefix: nameof(CheckFromPool));
        }
        catch (Exception ex)
        {
            log("could not speed up the craftable check, the game's own runs: " + ex.Message);
        }
    }

    /// <summary>Runs a held rebuild now. Called before this mod reads the workbench and once a frame.</summary>
    public static void Flush()
    {
        try
        {
            Batch.Flush(rebuild => Reducer_Web_ToolTable.RefreshRecipeList(rebuild.State, rebuild.Furniture, rebuild.Items));
        }
        catch (Exception ex)
        {
            Fail("running a held recipe list rebuild failed", ex);
        }
    }

    private static void Patch(Harmony harmony, string name, string? prefix = null, string? postfix = null)
    {
        harmony.Patch(AccessTools.Method(typeof(Reducer_Web_ToolTable), name),
            prefix: prefix == null ? null : new HarmonyMethod(typeof(RecipeRefresh), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(RecipeRefresh), postfix));
    }

    private static bool RunOrHold(State_Web_ToolTable __0, State_Data_Furniture __1, State_Data_Item __2, out bool __state)
    {
        __state = false;

        try
        {
            // Whatever the game would do with a missing state, it does itself.
            if (__0 == null || __2 == null) return true;

            if (!Batch.RunsNow(new Rebuild(__0, __1, __2)))
            {
                FrameProbe.End("recipe rebuild held", FrameProbe.Begin());

                return false;
            }

            __state = true;
            _rebuildsRunning++;
            _pool = null;
        }
        catch (Exception ex)
        {
            Fail("holding a recipe list rebuild failed", ex);
        }

        return true;
    }

    private static void Rebuilt(bool __state)
    {
        if (!__state) return;

        _rebuildsRunning--;
        _pool = null;
    }

    private static void FlushFirst() => Flush();

    private static void DropHeld() => Batch.Drop();

    /// <summary>
    /// Answers the craftable test from the pool while a rebuild runs. Storages do not change mid-rebuild, so the pool is
    /// read on the first recipe and used for the rest. The first answers are checked against the game's own, and one
    /// that differs turns the fast test off for the session.
    /// </summary>
    private static bool CheckFromPool(State_Web_ToolTable __0, State_Data_Item __1, Il2CppNeeded __2, ref bool __result)
    {
        if (_isAskingGame || !_isPoolTrusted || _rebuildsRunning == 0) return true;

        // The game answers false for these itself.
        if (__0?.WorkbenchOwnerId == null || __1?.OwnerCache == null || __2 == null) return true;

        try
        {
            if (_pool == null)
            {
                var startedAt = FrameProbe.Begin();

                _pool = ReadPool(__0, __1);
                FrameProbe.End("hub material pool", startedAt);
            }

            var covers = _pool.Covers(Needed(__2));

            if (_compared < ChecksCompared && !MatchesGame(__0, __1, __2, covers)) return true;

            __result = covers;

            return false;
        }
        catch (Exception ex)
        {
            _isPoolTrusted = false;
            Fail("the fast craftable check failed, the game's own is used from now on", ex);

            return true;
        }
    }

    private static bool MatchesGame(State_Web_ToolTable state, State_Data_Item items, Il2CppNeeded needed, bool covers)
    {
        bool game;

        _isAskingGame = true;

        try
        {
            game = Reducer_Web_ToolTable.CheckMaterialSufficiencyAcrossLinked(state, items, needed);
        }
        finally
        {
            _isAskingGame = false;
        }

        _compared++;

        if (game != covers)
        {
            _isPoolTrusted = false;
            _log($"the fast craftable check said {covers} where the game said {game} (needed {string.Join(", ", Needed(needed))}), the game's own is used from now on");

            return false;
        }

        if (_compared == ChecksCompared) _log($"the fast craftable check matched the game's on {ChecksCompared} recipes, it is used alone from now on");

        return true;
    }

    /// <summary>
    /// The storages the game would look in: its linked list with the bench added, duplicates kept, as it does. Every
    /// storage is read once before any is counted: a read moves items whose owner changed into their owner's list, so
    /// an item moved into a storage already counted would otherwise be missed.
    /// </summary>
    private static MaterialPool ReadPool(State_Web_ToolTable state, State_Data_Item items)
    {
        var owners = new List<long>();
        var linked = Reducer_Web_ToolTable.GetAllLinkedOwnerIds(state);

        for (var i = 0; linked != null && i < linked.Count; i++) owners.Add(linked[i]);

        owners.Add(state.WorkbenchOwnerId.Value);

        foreach (var owner in new HashSet<long>(owners)) State_Extensions_Item.GetItemDataList(items, owner);

        var held = new Dictionary<long, List<(int ItemId, int Count)>>();
        var stacks = new List<(int ItemId, int Count)>();

        foreach (var owner in owners)
        {
            if (!held.TryGetValue(owner, out var ownerStacks)) held[owner] = ownerStacks = Stacks(items, owner);

            stacks.AddRange(ownerStacks);
        }

        return MaterialPool.From(stacks);
    }

    /// <summary>Read through the game's own list call, which also tidies items whose owner moved on.</summary>
    private static List<(int ItemId, int Count)> Stacks(State_Data_Item items, long owner)
    {
        var stacks = new List<(int ItemId, int Count)>();
        var list = State_Extensions_Item.GetItemDataList(items, owner);

        for (var i = 0; list != null && i < list.Count; i++)
        {
            var item = list[i];

            if (item != null) stacks.Add((item.ItemConfigId, item.ItemCount));
        }

        return stacks;
    }

    private static List<KeyValuePair<int, int>> Needed(Il2CppNeeded needed)
    {
        var materials = new List<KeyValuePair<int, int>>(needed.Count);

        foreach (var material in needed) materials.Add(new KeyValuePair<int, int>(material.Key, material.Value));

        return materials;
    }

    private static void Fail(string what, Exception ex)
    {
        _failures++;

        if (_failures <= MaxLoggedFailures) _log($"{what} (logged {_failures} of {MaxLoggedFailures}): {ex}");
    }
}
