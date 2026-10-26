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

    /// <summary>One look at the storages: the stacks they hold, and the storages the game's own rule is asked about.</summary>
    private sealed record Storages(MaterialPool Pool, long[] Owners);

    private static readonly RefreshBatch<Rebuild> Batch = new();
    private static readonly List<KeyValuePair<int, int>> NeededBuffer = new();
    private static Action<string> _log = _ => { };
    private static int _rebuildsRunning;
    private static Storages? _storages;
    private static bool _isPoolTrusted = true;
    private static bool _hasLoggedReread;
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
            log("could not hook the workbench recipe list rebuilds, the game rebuilds it as before: " + ex.Message);
        }

        try
        {
            Patch(harmony, nameof(Reducer_Web_ToolTable.CheckMaterialSufficiencyAcrossLinked), prefix: nameof(CheckFromPool));
        }
        catch (Exception ex)
        {
            log("could not hook the craftable check, the game's own runs: " + ex.Message);
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

            if (!Batch.RunsNow(new Rebuild(__0, __1, __2))) return false;

            __state = true;
            _rebuildsRunning++;
            _storages = null;
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
        _storages = null;
    }

    private static void FlushFirst() => Flush();

    private static void DropHeld() => Batch.Drop();

    /// <summary>
    /// Answers the craftable test from the pool while a rebuild runs. Storages do not change mid-rebuild, so the pool is
    /// read on the first recipe and used for the rest. The first answers are checked against the game's own rule, and one
    /// that differs turns the fast test off for the session. Other mods' postfixes on the check still run on the answer.
    /// </summary>
    private static bool CheckFromPool(State_Web_ToolTable __0, State_Data_Item __1, Il2CppNeeded __2, ref bool __result)
    {
        if (!_isPoolTrusted || _rebuildsRunning == 0) return true;

        // The game answers false for these itself.
        if (__0?.WorkbenchOwnerId == null || __1?.OwnerCache == null || __2 == null) return true;

        try
        {
            _storages ??= ReadStorages(__0, __1);

            var covers = _storages.Pool.Covers(Needed(__2));

            if (_compared < ChecksCompared && !MatchesGame(_storages, __0, __1, __2, covers)) return true;

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

    /// <summary>
    /// Asks the game's rule itself, under its check: other mods hook the check (Stacking turns a no into a yes over
    /// units), and comparing against their answer would switch the fast test off for nothing.
    /// </summary>
    private static bool MatchesGame(Storages storages, State_Web_ToolTable state, State_Data_Item items, Il2CppNeeded needed, bool covers)
    {
        var game = GameRule(items, needed, storages.Owners);

        _compared++;

        // On a miss the game's own runs for this recipe; the fast check stays on only if a fresh read explains the miss.
        if (game != covers)
        {
            if (IsFixedByRereading(state, items, needed)) return false;

            _isPoolTrusted = false;
            _log($"the fast craftable check said {covers} where the game's rule said {game} (needed {string.Join(", ", Needed(needed))}), the game's own is used from now on");

            return false;
        }

        if (_compared == ChecksCompared) _log($"the fast craftable check matched the game's on {ChecksCompared} recipes, it is used alone from now on");

        return true;
    }

    private static bool GameRule(State_Data_Item items, Il2CppNeeded needed, long[] owners) =>
        Reducer_Web_ToolTable.SelectItemsForRecipe(items, needed, owners) != null;

    /// <summary>
    /// Items can change while a rebuild runs (another mod, or a move the game finishes mid-rebuild), which leaves the
    /// pool read on its first recipe out of date. When a fresh read agrees with the game, the fresh one is kept.
    /// </summary>
    private static bool IsFixedByRereading(State_Web_ToolTable state, State_Data_Item items, Il2CppNeeded needed)
    {
        var fresh = ReadStorages(state, items);

        if (fresh.Pool.Covers(Needed(needed)) != GameRule(items, needed, fresh.Owners)) return false;

        _storages = fresh;

        if (!_hasLoggedReread)
        {
            _hasLoggedReread = true;
            _log("the storages changed during a recipe list rebuild, the fast craftable check read them again");
        }

        return true;
    }

    /// <summary>
    /// The storages the game would look in: its linked list with the bench added, duplicates kept, as it does. Every
    /// storage is read once before any is counted: a read moves items whose owner changed into their owner's list, so
    /// an item moved into a storage already counted would otherwise be missed.
    /// </summary>
    private static Storages ReadStorages(State_Web_ToolTable state, State_Data_Item items)
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

        return new Storages(MaterialPool.From(stacks), owners.ToArray());
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

    /// <summary>One buffer for every recipe a rebuild checks; each answer is used before the next is read.</summary>
    private static List<KeyValuePair<int, int>> Needed(Il2CppNeeded needed)
    {
        NeededBuffer.Clear();

        foreach (var material in needed) NeededBuffer.Add(new KeyValuePair<int, int>(material.Key, material.Value));

        return NeededBuffer;
    }

    private static void Fail(string what, Exception ex)
    {
        _failures++;

        if (_failures <= MaxLoggedFailures) _log($"{what} (logged {_failures} of {MaxLoggedFailures}): {ex}");
    }
}
