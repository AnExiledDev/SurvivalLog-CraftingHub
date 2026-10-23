using System;
using System.Collections.Generic;
using System.Linq;
using CraftingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CraftingHub.Adapters;

/// <summary>The open workbench's recipes, what the house holds of their materials, and which the game allows now.</summary>
internal sealed record BenchRead(RecipeBook Book, IReadOnlyDictionary<int, int> Stock, IReadOnlySet<string> CraftableNow, IReadOnlyList<DyeOption> Dyes);

/// <summary>A colored recipe as read from the production table, before the house's dye is counted.</summary>
internal sealed record DyedVariant(Recipe Recipe, string Hex, string Color);

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

    /// <summary>Stamina is worked out from the player each time it is asked, so it is read at most this often.</summary>
    private const long StaminaReadMs = 500;

    private static State_Web_ToolTable? _state;
    private static int _refreshes;
    private static bool _countsResults;
    private static bool _resultsProven;
    private static Action<string> _hookLog = _ => { };
    private static readonly Dictionary<long, ResultCount> ResultsByBench = new();

    private readonly Action<string> _log;
    private readonly Dictionary<string, Recipe?> _recipes = new();
    private readonly Dictionary<string, IReadOnlyList<DyedVariant>> _variants = new();
    private bool _hasLoggedVariantFailure;
    private readonly Dictionary<int, string> _itemNames = new();
    private bool _hasLoggedYield;
    private StaminaRead? _stamina;
    private readonly ReadThrottle _staminaReads = new(StaminaReadMs);
    private bool _hasLoggedStaminaFailure;
    private bool _hasLoggedStamina;
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
            log("could not hook the workbench recipe list, chain marks and the queue will not work: " + ex.Message);
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
            log("could not hook finished crafts, a step is credited from the batch count alone: " + ex.Message);
        }
    }

    public bool IsOpen => _state != null && GameSingleton.Existing<ReduxUISystem>()?.IsUIActive(Il2CppType.Of<WebUI_ToolTable>()) == true;

    public BenchRead? Read()
    {
        RecipeRefresh.Flush();

        var state = _state;
        var items = ItemState();

        // The kept state outlives its window, and after a quit to title it belongs to the old world.
        if (state?.RecipeList == null || items == null || !IsOpen) return null;

        var recipes = new List<Recipe>(state.RecipeList.Count);
        var craftable = new HashSet<string>();
        var colored = new List<(Recipe Base, DyedVariant Variant)>();
        var fixedParts = FixedParts(state);

        for (var i = 0; i < state.RecipeList.Count; i++)
        {
            var row = state.RecipeList[i];
            var key = row?.RecipeKey?.Value;

            if (row == null || string.IsNullOrEmpty(key) || ConfigRecipe(key) is not { } recipe) continue;

            // The game sends a recipe's name and dye flag in a list of their own and leaves them empty on this row.
            var fixedPart = fixedParts.GetValueOrDefault(key);
            var name = fixedPart.Name ?? NonEmpty(row.Name?.Value) ?? recipe.Name;
            var listed = recipe with { Name = name, IsLocked = row.CraftLocked?.Value == true };

            recipes.Add(listed);

            if (row.CanCraft?.Value == true) craftable.Add(key);

            if (fixedPart.IsDyeable || row.Dyeable?.Value == true) colored.AddRange(Variants(key, listed).Select(variant => (listed, variant)));
        }

        // Base recipes go in first, so a product both make is planned from the plain recipe.
        var book = new RecipeBook(recipes.Concat(colored.Select(pair => pair.Variant.Recipe)));

        LogYieldOnce(book);

        var stock = Stock(state, items, book.MaterialIds);
        var dyes = new List<DyeOption>(colored.Count);

        foreach (var (plain, variant) in colored)
        {
            var recipe = variant.Recipe;
            var need = recipe.Materials.GetValueOrDefault(recipe.DyeId);
            var have = stock.GetValueOrDefault(recipe.DyeId);

            dyes.Add(new DyeOption(plain.Key, recipe.Key, recipe.DyeId, variant.Hex, variant.Color, need, have));

            // The game only says whether the plain recipe can be made; the colored one also takes its dye.
            if (craftable.Contains(plain.Key) && have >= need) craftable.Add(recipe.Key);
        }

        return new BenchRead(book, stock, craftable, dyes);
    }

    /// <summary>
    /// The colors a dyeable recipe comes in, as the game's own dye bar lists them. The game keeps these recipes off its
    /// list, so they are read from the production table once per base and fill the bench by their own key.
    /// </summary>
    private IReadOnlyList<DyedVariant> Variants(string baseKey, Recipe plain)
    {
        if (!_variants.TryGetValue(baseKey, out var cached)) cached = _variants[baseKey] = ReadVariants(baseKey);

        return cached.Select(variant => variant with { Recipe = variant.Recipe with { IsLocked = plain.IsLocked, Name = ColoredName(plain.Name, variant) } }).ToList();
    }

    private IReadOnlyList<DyedVariant> ReadVariants(string baseKey)
    {
        var found = new List<DyedVariant>();

        try
        {
            var config = GameSingleton.Existing<ConfigManager>();
            var map = config?.GetAllProductionListMap();

            if (config == null || map == null || !map.TryGetValue(baseKey, out var production) || production == null) return found;

            // Handed back as a read-only view, but the object is the game's own list.
            var dyed = config.GetDyedVariants(production.ID)?.TryCast<Il2CppSystem.Collections.Generic.List<Config_ProductionList>>();

            for (var i = 0; dyed != null && i < dyed.Count; i++)
            {
                var variant = dyed[i];
                var dyeId = variant == null ? 0 : config.GetDyedRecipeDyeItemId(variant.ID);

                if (variant?.ProductID is not { Count: > 0 } products || variant.MaterialList is not { Count: > 0 } list || dyeId <= 0) continue;

                var materialIds = new List<int>(list.Count);

                for (var m = 0; m < list.Count; m++) materialIds.Add(list[m]);

                var productId = products[0];
                var yield = 0;

                for (var p = 0; p < products.Count; p++) yield += products[p] == productId ? 1 : 0;

                var materials = materialIds.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
                var recipe = new Recipe(RecipeBook.KeyOf(materialIds), ItemName(productId), productId, yield, materials, IsLocked: false, BaseKey: baseKey, DyeId: dyeId);

                found.Add(new DyedVariant(recipe, NonEmpty(DyePaletteRules.GetDyeHex(dyeId)) ?? "#888888", NonEmpty(GameKey.GetDyeColorName(dyeId)) ?? ItemName(dyeId)));
            }
        }
        catch (Exception ex)
        {
            if (!_hasLoggedVariantFailure) _log($"could not read the colors of recipe {baseKey}, it is offered plain only: " + ex.Message);

            _hasLoggedVariantFailure = true;
        }

        return found;
    }

    /// <summary>The colored item's own name when the game gives it one, else the plain name with the color after it.</summary>
    private static string ColoredName(string plainName, DyedVariant variant) =>
        variant.Recipe.Name.Length > 0 && variant.Recipe.Name != plainName && !variant.Recipe.Name.StartsWith("Item ", StringComparison.Ordinal)
            ? variant.Recipe.Name
            : $"{plainName} ({variant.Color})";

    public BenchView View()
    {
        RecipeRefresh.Flush();

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
    /// The player's stamina and one craft's cost; null when the workbench is closed or the game does not answer. The
    /// first reading is logged, so a page without stamina can be told apart from a game that answered nothing.
    /// </summary>
    public StaminaRead? Stamina()
    {
        if (_state == null || !IsOpen) return null;

        if (!_staminaReads.IsDue(Environment.TickCount64)) return _stamina;

        _stamina = ReadStamina();

        if (!_hasLoggedStamina) LogFirstStamina(_stamina);

        return _stamina;
    }

    private void LogFirstStamina(StaminaRead? read)
    {
        _hasLoggedStamina = true;

        _log(read is { } stamina ? $"stamina read: have {stamina.Current:0.#}, a craft costs {stamina.PerCraft:0.#}" : "stamina read: the game gave no answer");
    }

    /// <summary>
    /// Asked of the game's tool table directly, the code the workbench's own stamina query ends in. The query goes
    /// through the game's event dispatch, and from here that answered nothing (zero for both), so no stamina showed.
    /// </summary>
    private StaminaRead? ReadStamina()
    {
        try
        {
            var tools = GameSingleton.Existing<BattleLogicWorld>()?._ToolTableManager;

            if (tools == null || !tools.GetCraftStamina(out var current, out var required)) return null;

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

            recipe = new Recipe(key, ItemName(productId), productId, yield, materials, IsLocked: false);
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

    /// <summary>
    /// Each recipe's name and dye flag by recipe key: from the list the game sends when the window opens or the recipes
    /// shown change, then from the game's cache of it for a key the list lacks. Empty when neither reads, and names
    /// then come from the item table.
    /// </summary>
    private static Dictionary<string, (string? Name, bool IsDyeable)> FixedParts(State_Web_ToolTable state)
    {
        var found = new Dictionary<string, (string? Name, bool IsDyeable)>();

        try
        {
            var list = state.RecipeStaticList;

            for (var i = 0; list != null && i < list.Count; i++)
            {
                var row = list[i];
                var key = row?.RecipeKey?.Value;

                if (row != null && !string.IsNullOrEmpty(key)) found[key] = (NonEmpty(row.Name?.Value), row.Dyeable?.Value == true);
            }

            var cache = state.RecipeStaticCache;

            if (cache == null) return found;

            foreach (var pair in cache)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null || found.TryGetValue(pair.Key, out var listed) && listed.Name != null) continue;

                found[pair.Key] = (NonEmpty(pair.Value.Name), pair.Value.Dyeable);
            }
        }
        catch (Exception)
        {
            // A layout this build does not know: names fall back to the item table.
        }

        return found;
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
