using System;
using System.Collections.Generic;
using System.Linq;
using CraftingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CraftingHub.Adapters;

/// <summary>The open workbench's recipes, what the house holds of their materials, and which the game allows now.</summary>
internal sealed record BenchRead(RecipeBook Book, IReadOnlyDictionary<int, int> Stock, IReadOnlySet<string> CraftableNow);

/// <summary>
/// The only code that reads the workbench. The game hands its window state to RefreshRecipeList whenever the recipe
/// list changes (opening, crafting, items moving), so a postfix keeps that state and counts the refreshes; the ticker
/// rereads when the count moves.
/// </summary>
internal sealed class Workbench
{
    /// <summary>The game's HandMadeState values for a finished craft: 2 succeeded, 3 failed.</summary>
    private const int Succeeded = 2;
    private const int Failed = 3;

    /// <summary>Stamina is worked out through a game event, so it is read at most this often.</summary>
    private const long StaminaReadMs = 500;

    private static State_Web_ToolTable? _state;
    private static int _refreshes;
    private static bool _countsResults;
    private static bool _resultsProven;
    private static Action<string> _hookLog = _ => { };
    private static readonly Dictionary<long, ResultCount> ResultsByBench = new();

    private readonly Action<string> _log;
    private readonly Dictionary<string, Recipe?> _recipes = new();
    private readonly Dictionary<int, string> _itemNames = new();
    private bool _hasLoggedYield;
    private StaminaRead? _stamina;
    private long _staminaReadAt = long.MinValue;
    private bool _hasLoggedStaminaFailure;
    private bool _hasLoggedUnproven;

    public Workbench(Action<string> log)
    {
        _log = log;
    }

    /// <summary>Goes up every time the game rebuilds the recipe list.</summary>
    public int Refreshes => _refreshes;

    public static void Apply(Harmony harmony, Action<string> log)
    {
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Reducer_Web_ToolTable), nameof(Reducer_Web_ToolTable.RefreshRecipeList)),
                postfix: new HarmonyMethod(typeof(Workbench), nameof(RememberState)));
            log("watching the workbench recipe list");
        }
        catch (Exception ex)
        {
            log("could not watch the workbench recipe list, chain marks and the queue will not work: " + ex.Message);
        }

        try
        {
            harmony.Patch(AccessTools.Method(typeof(LogicAdapter), "OnHandMadeResult"),
                postfix: new HarmonyMethod(typeof(Workbench), nameof(CountResult)));
            _countsResults = true;
            _hookLog = log;
            log("watching for finished crafts");
        }
        catch (Exception ex)
        {
            log("could not count finished crafts, a step is credited from the batch count alone: " + ex.Message);
        }
    }

    public bool IsOpen => _state != null && GameSingleton.Existing<ReduxUISystem>()?.IsUIActive(Il2CppType.Of<WebUI_ToolTable>()) == true;

    public BenchRead? Read()
    {
        var state = _state;
        var items = ItemState();

        // The kept state outlives its window, and after a quit to title it belongs to the old world.
        if (state?.RecipeList == null || items == null || !IsOpen) return null;

        var recipes = new List<Recipe>(state.RecipeList.Count);
        var craftable = new HashSet<string>();

        for (var i = 0; i < state.RecipeList.Count; i++)
        {
            var row = state.RecipeList[i];
            var key = row?.RecipeKey?.Value;

            if (row == null || string.IsNullOrEmpty(key) || ConfigRecipe(key) is not { } recipe) continue;

            recipes.Add(recipe with { Name = row.Name?.Value ?? recipe.Name, IsLocked = row.CraftLocked?.Value == true });

            if (row.CanCraft?.Value == true) craftable.Add(key);
        }

        var book = new RecipeBook(recipes);

        LogYieldOnce(book);

        return new BenchRead(book, Stock(state, items, book.MaterialIds), craftable);
    }

    public BenchView View()
    {
        var state = _state;

        if (state == null) return new BenchView(false, false, false, 0, new Dictionary<int, int>(), 0);

        // A craft can finish in the moment before the window closes; the kept state still holds its count.
        if (!IsOpen) return new BenchView(false, false, false, 0, new Dictionary<int, int>(), state.BatchDone, BatchActive: state.BatchActive, BatchSuccess: state.BatchSuccess, Results: Results(state.CurrentFurnitureId), ResultsProven: _resultsProven);

        var handState = (int)state.HandMadeState;

        return new BenchView(
            IsOpen: true,
            IsBusy: state.IsCrafting?.Value == true || state.BatchActive,
            // The game's own test for a live craft (HandMadeState 1, Making), told apart from the batch flag.
            IsCrafting: state.IsCrafting?.Value == true || handState == 1,
            HasResult: state.ResultShow?.Value == true,
            MaxCrafts: state.MaxCraftCount?.Value ?? 0,
            Contents: Contents(state.WorkbenchOwnerId?.Value ?? 0),
            BatchDone: state.BatchDone,
            BenchId: state.CurrentFurnitureId,
            BatchActive: state.BatchActive,
            BatchSuccess: state.BatchSuccess,
            IsHandHeld: handState is Succeeded or Failed,
            PreviewReady: state.PreviewShow?.Value == true && state.PreviewUnknown?.Value != true,
            MaxByStamina: state.MaxByStamina?.Value == true,
            CraftLocked: state.CraftBtnLocked?.Value == true,
            LockText: state.CraftBtnLockText?.Value ?? "",
            Stamina: Stamina(),
            Results: Results(state.CurrentFurnitureId),
            Refreshes: _refreshes,
            ResultsProven: _resultsProven);
    }

    /// <summary>
    /// The player's stamina and one craft's cost, asked of the game the way the workbench asks for its own count;
    /// null when the workbench is closed or the game does not answer.
    /// </summary>
    public StaminaRead? Stamina()
    {
        var state = _state;

        if (state == null || !IsOpen) return null;

        var now = Environment.TickCount64;

        if (now - _staminaReadAt < StaminaReadMs) return _stamina;

        _staminaReadAt = now;
        _stamina = ReadStamina(state);

        return _stamina;
    }

    private StaminaRead? ReadStamina(State_Web_ToolTable state)
    {
        try
        {
            Reducer_Web_ToolTable.QueryCraftStamina(state, out var current, out var required);

            return new StaminaRead(current, required);
        }
        catch (Exception ex)
        {
            if (!_hasLoggedStaminaFailure) _log("could not read stamina, it is left off the page: " + ex.Message);

            _hasLoggedStaminaFailure = true;

            return null;
        }
    }

    /// <summary>
    /// Called as a step ends. Before the game first hands over a finished craft the result count is not trusted, and
    /// that is said once.
    /// </summary>
    public void NoteStepDone()
    {
        if (!_countsResults || _resultsProven || _hasLoggedUnproven) return;

        _hasLoggedUnproven = true;

        _log("a step finished before the finished-crafts counter first fired; until it does, steps are credited from the batch count or as sent");
    }

    private static ResultCount? Results(long benchId) =>
        !_countsResults ? null : ResultsByBench.TryGetValue(benchId, out var count) ? count : new ResultCount(0, 0);

    public string ItemName(int configId)
    {
        if (_itemNames.TryGetValue(configId, out var cached)) return cached;

        var config = GameSingleton.Existing<ConfigManager>();
        var item = config?.Get_Config_Item(configId);
        var local = item == null || config == null || string.IsNullOrEmpty(item.ItemName) ? null : config.GetLocalTxt(item.ItemName);
        var name = !string.IsNullOrEmpty(local) && local != item?.ItemName ? local : NonEmpty(item?.ItemName_Local) ?? NonEmpty(item?.ItemName) ?? $"Item {configId}";

        return _itemNames[configId] = name;
    }

    private static void RememberState(State_Web_ToolTable state)
    {
        _state = state;
        _refreshes++;
    }

    /// <summary>
    /// Counts each craft the game finishes, per workbench. The game's crafting loop hands every finished craft to this
    /// one handler, window open or not, before it picks the window's result action, so a craft that finishes after
    /// the window closes is counted too. Batch or not, every craft posts its own result, so this sees crafts the batch
    /// count misses. Arguments are taken by position: the bench's id, then the outcome.
    /// </summary>
    private static void CountResult(long __0, HandMadeState __1)
    {
        try
        {
            ProveResults();

            var outcome = (int)__1;

            if (outcome is not (Succeeded or Failed)) return;

            var count = ResultsByBench.TryGetValue(__0, out var seen) ? seen : new ResultCount(0, 0);

            ResultsByBench[__0] = new ResultCount(count.All + 1, count.Successes + (outcome == Succeeded ? 1 : 0));
        }
        catch
        {
            // A count missed here only means a step is credited from the batch count; never break the game's result.
        }
    }

    private static void ProveResults()
    {
        if (_resultsProven) return;

        _resultsProven = true;

        _hookLog("finished crafts are counted");
    }

    private static State_Data_Item? ItemState() => GameSingleton.Existing<ReduxUISystem>()?.GetData<State_Data_Item>(Il2CppType.Of<State_Data_Item>());

    /// <summary>Counted the game's own way (it skips spoiled food) over the same storages it would craft from.</summary>
    private static Dictionary<int, int> Stock(State_Web_ToolTable state, State_Data_Item items, IEnumerable<int> materialIds)
    {
        var owners = new HashSet<long> { state.WorkbenchOwnerId?.Value ?? 0 };
        var linked = Reducer_Web_ToolTable.GetAllLinkedOwnerIds(state);

        for (var i = 0; linked != null && i < linked.Count; i++) owners.Add(linked[i]);

        owners.Remove(0);

        var pool = new Il2CppStructArray<long>(owners.ToArray());

        return materialIds.ToDictionary(id => id, id => Reducer_Web_ToolTable.CountOwnedInPool(items, id, pool));
    }

    private static Dictionary<int, int> Contents(long benchOwner)
    {
        var contents = new Dictionary<int, int>();
        var cache = ItemState()?.OwnerCache;

        if (benchOwner == 0 || cache == null || !cache.TryGetValue(benchOwner, out var held) || held == null) return contents;

        foreach (var item in held.Values)
        {
            // The cache can keep stale entries whose owner moved on; the game ignores those, so do we.
            if (item == null || item.OwnerId != benchOwner) continue;

            contents[item.ItemConfigId] = contents.GetValueOrDefault(item.ItemConfigId) + item.ItemCount;
        }

        return contents;
    }

    /// <summary>Materials and product come from the production table and never change, so each key is read once.</summary>
    private Recipe? ConfigRecipe(string key)
    {
        if (_recipes.TryGetValue(key, out var cached)) return cached;

        var map = GameSingleton.Existing<ConfigManager>()?.GetAllProductionListMap();
        Recipe? recipe = null;

        if (map != null && map.TryGetValue(key, out var production) && production?.ProductID is { Count: > 0 } products && production.MaterialList != null)
        {
            var productId = products[0];
            var yield = 0;
            var materials = new Dictionary<int, int>();

            for (var i = 0; i < products.Count; i++) yield += products[i] == productId ? 1 : 0;

            for (var i = 0; i < production.MaterialList.Count; i++) materials[production.MaterialList[i]] = materials.GetValueOrDefault(production.MaterialList[i]) + 1;

            recipe = new Recipe(key, $"Recipe {key}", productId, yield, materials, IsLocked: false);
        }
        else
        {
            _log($"recipe {key} is not in the production table, it is left out of planning");
        }

        return _recipes[key] = recipe;
    }

    /// <summary>
    /// The yield rule (one product per entry in ProductID) is read off the table's shape, not the game's code, so the
    /// recipes it says make more than one are logged once for checking against what a craft actually gives.
    /// </summary>
    private void LogYieldOnce(RecipeBook book)
    {
        if (_hasLoggedYield) return;

        _hasLoggedYield = true;

        var multi = book.All.Where(recipe => recipe.Yield > 1).Take(8).Select(recipe => $"{recipe.Name} ×{recipe.Yield}").ToList();

        _log(multi.Count == 0 ? "every recipe at this bench makes one item per craft" : "recipes read as making several per craft (check one in game): " + string.Join(", ", multi));
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
