using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>One piece of on-screen text in the two languages the game ships. {0}, {1} are filled in by Say.</summary>
public readonly record struct Phrase(string En, string Zh)
{
    public string Pick(bool isChinese) => isChinese ? Zh : En;

    public string Say(bool isChinese, params object[] args) => string.Format(CultureInfo.InvariantCulture, Pick(isChinese), args);
}

/// <summary>The words the mod puts on screen: the queue's status lines, the heads-up and order notes, and the panel's own labels.</summary>
public static class Texts
{
    private const string Log = "BepInEx/crafting-hub.log";

    // Why a run paused, carried on or stopped.
    public static readonly Phrase Closed = new("the workbench closed; the queue is kept, press Craft queue to carry on", "工作台已关闭；队列已保留，点「制作队列」继续");
    public static readonly Phrase Paused = new("the workbench closed; the queue carries on when you open it again", "工作台已关闭；再次打开它时队列会继续");
    public static readonly Phrase PausedAnyBench = new("the workbench closed; the queue carries on when you open any workbench", "工作台已关闭；打开任意工作台时队列会继续");
    public static readonly Phrase Resumed = new("the workbench is open again; carrying on with the queue", "工作台已重新打开，继续制作队列");
    public static readonly Phrase ResumedAnyBench = new("a workbench is open; carrying on with the queue here", "已打开工作台，在这里继续制作队列");
    public static readonly Phrase Elsewhere = new("the queue carries on at the workbench it was running on; press Stop, then Craft queue, to run it here", "队列会在原来的工作台继续；先点「停止」，再点「制作队列」，就能在这里运行");
    public static readonly Phrase StaleBatch = new("the workbench shows a batch in progress with nothing being made; carrying on with the next craft", "工作台显示有一批正在制作，但实际没有在做；继续下一次制作");
    public static readonly Phrase Busy = new("waiting for the workbench: it has shown a craft in progress for a while with no change", "正在等待工作台：它显示制作中已有一段时间，没有变化");
    public static readonly Phrase StuckPaused = new("the workbench is stuck after the last craft; close it and open it again to carry on with the queue", "上一次制作后工作台卡住了；关掉再打开即可继续队列");
    public static readonly Phrase StuckStopped = new("the workbench is stuck after the last craft; close it and open it again, then press Craft queue", "上一次制作后工作台卡住了；关掉再打开，然后点「制作队列」");
    public static readonly Phrase QueueFinished = new("queue finished", "队列已完成");
    public static readonly Phrase TooManyIntermediates = new("stopped after {0} intermediate crafts without finishing the target", "已制作 {0} 次中间材料仍未完成目标，已停止");
    public static readonly Phrase NothingToCraft = new("nothing to craft", "没有可制作的东西");
    public static readonly Phrase LockedBecause = new("{0} is locked: {1}", "{0}已锁定：{1}");
    public static readonly Phrase Locked = new("{0} is locked", "{0}已锁定");
    public static readonly Phrase OutOfStaminaCost = new("Out of stamina: {0} needs {1} per craft, you have {2}", "精力不足：{0}每次制作需要 {1}，你现在有 {2}");
    public static readonly Phrase OutOfStamina = new("Out of stamina: there is not enough stamina for a craft of {0}", "精力不足：不够制作一次{0}");
    public static readonly Phrase NotLaidOut = new("the game did not lay out the materials for {0}", "游戏没有摆好{0}的材料");
    public static readonly Phrase NoCraftAllowed = new("the materials for {0} are laid out but the game allows no craft of it", "{0}的材料已摆好，但游戏不允许制作");
    public static readonly Phrase TooMuchOnBench = new("the workbench holds more than one craft's worth of materials for {0}; the game crafts only from exactly one craft's worth, so take the extra off the workbench", "工作台上{0}的材料超过一次制作的量；游戏只用正好一次的量制作，请把多余的从工作台上拿走");
    public static readonly Phrase DidNotStart = new("the craft of {0} did not start", "{0}的制作没有开始");
    public static readonly Phrase ResultStuck = new("the result screen did not close", "结果界面没有关闭");
    public static readonly Phrase NothingMadeTwice = new("the game made nothing from {0} twice in a row; press Craft queue to try again", "游戏连续两次没有做出{0}；点「制作队列」重试");
    public static readonly Phrase NothingMadeOnce = new("the game made nothing from the {0} craft just sent; trying again", "刚发出的{0}制作没有做出东西；正在重试");

    // What the next step of the queue is waiting on.
    public static readonly Phrase NotListed = new("{0} is not in your recipe list yet", "{0}还不在你的配方列表里");
    public static readonly Phrase NotUnlocked = new("{0} is not unlocked yet", "{0}还没有解锁");
    public static readonly Phrase CannotMake = new("can't make {0} ×{1}: short of {2}", "无法制作{0} ×{1}：缺少{2}");
    public static readonly Phrase NoPlan = new("no plan for {0}", "无法为{0}安排制作");
    public static readonly Phrase CouldNotReadBench = new("could not read the workbench", "无法读取工作台");

    // The queue's forecast: a note per order, and one heads-up line.
    public static readonly Phrase HeadsUpShort = new("Heads up: {0} is short of {1}. The queue will stop there.", "注意：{0}缺少{1}。队列会在这里停下。");
    public static readonly Phrase HeadsUpLocked = new("Heads up: {0} is not unlocked yet. The queue will stop there.", "注意：{0}还没有解锁。队列会在这里停下。");
    public static readonly Phrase HeadsUpNotListed = new("Heads up: {0} is not in your recipe list yet. The queue will stop there.", "注意：{0}还不在你的配方列表里。队列会在这里停下。");
    public static readonly Phrase NoteNotListed = new("Not in your recipe list yet", "还不在你的配方列表里");
    public static readonly Phrase NoteLocked = new("Not unlocked yet", "还没有解锁");
    public static readonly Phrase NoteShort = new("Short: {0}", "缺少：{0}");
    public static readonly Phrase MakesFirst = new("Makes first: {0}", "先制作：{0}");
    public static readonly Phrase Ready = new("Ready", "就绪");

    // Keeping items in stock.
    public static readonly Phrase WaitsCannotMake = new("the queue holds {0}, which this workbench cannot make", "队列里有{0}，这个工作台做不了");
    public static readonly Phrase WaitsLocked = new("the queue holds {0}, which is not unlocked yet", "队列里有{0}，它还没有解锁");
    public static readonly Phrase KeepWaits = new("Keep stocked waits: {0}", "保持库存暂缓：{0}");
    public static readonly Phrase KeepQueued = new("Keep stocked: queued {0}", "保持库存：已加入队列 {0}");
    public static readonly Phrase KeepShort = new("Keep stocked: not enough materials for {0}", "保持库存：材料不够制作{0}");
    public static readonly Phrase KeepQueuedAndShort = new("Keep stocked: queued {0}; not enough materials for {1}", "保持库存：已加入队列 {0}；材料不够制作{1}");
    public static readonly Phrase Keeping = new("Keeping {0} {1} in stock", "保持{1}库存 {0} 个");
    public static readonly Phrase NoLongerKeeping = new("No longer keeping {0} in stock", "不再保持{0}的库存");
    public static readonly Phrase NoSaveLoaded = new("no save is loaded, so the target was not kept", "没有载入存档，所以没有保存这个目标");
    public static readonly Phrase TargetsUnreadable = new("the targets file could not be read, so nothing was changed (see the log)", "无法读取目标文件，所以没有做任何修改（详见日志）");
    public static readonly Phrase NotSaved = new("{0} (could not save it to the targets file, see the log)", "{0}（无法保存到目标文件，详见日志）");
    public static readonly Phrase TargetsFileUnreadable = new("the keep-stocked targets file could not be read (see the log)", "无法读取保持库存的目标文件（详见日志）");
    public static readonly Phrase NotListedRecipe = new("that recipe is not in your recipe list yet", "这个配方还不在你的配方列表里");

    // The queue's buttons and the session around them.
    public static readonly Phrase RepeatedErrors = new($"the craft queue stopped after repeated errors; details are in {Log}", $"制作队列多次出错后已停止；详情见 {Log}");
    public static readonly Phrase Queued = new("Queued {0} ×{1}", "已加入队列：{0} ×{1}");
    public static readonly Phrase QueueEmpty = new("the queue is empty", "队列是空的");
    public static readonly Phrase Running = new("running the queue", "正在运行队列");
    public static readonly Phrase RefusedHere = new("the queue is paused and carries on here in a moment; press Stop first to craft this now", "队列已暂停，稍后会在这里继续；想现在制作这个，请先点「停止」");
    public static readonly Phrase RefusedElsewhere = new("the queue is paused and carries on at the workbench it was running on; press Stop first to craft this now", "队列已暂停，会在原来的工作台继续；想现在制作这个，请先点「停止」");
    public static readonly Phrase MakingNext = new("Making {0} x{1} next, once the craft in progress is done", "当前制作完成后，接着制作{0} x{1}");
    public static readonly Phrase MakingNow = new("Making {0} ×{1} with its intermediates", "正在制作{0} ×{1}，包括它的中间材料");
    public static readonly Phrase OrderRemoved = new("stopped: the running order was removed", "已停止：正在执行的订单被移除了");
    public static readonly Phrase Crafting = new("Crafting {0} ×{1}{2}", "正在制作{0} ×{1}{2}");
    public static readonly Phrase ForOrder = new(" for {0}", "（为{0}）");
    public static readonly Phrase Stopped = new("stopped", "已停止");
    public static readonly Phrase QueueCleared = new("queue cleared", "队列已清空");
    public static readonly Phrase AnotherSave = new("another save was loaded", "载入了另一个存档");

    /// <summary>The panel's labels, by the key page.js asks for them with.</summary>
    public static readonly IReadOnlyList<(string Key, Phrase Phrase)> Page = new (string, Phrase)[]
    {
        ("filterAll", new("All", "全部")),
        ("filterNow", new("Can make now", "现在能做")),
        ("filterVia", new("Needs parts first", "需先做材料")),
        ("filterShort", new("Short", "材料不足")),
        ("filterKept", new("Kept in stock", "保持库存")),
        ("oneLess", new("One less", "少一个")),
        ("oneMore", new("One more", "多一个")),
        ("locked", new("Locked", "已锁定")),
        ("canMakeNow", new("Can make now", "现在能做")),
        ("shortOf", new("Short: {0}", "缺少：{0}")),
        ("cannotMake", new("Cannot make now", "现在做不了")),
        ("clearSearch", new("Clear the search", "清空搜索")),
        ("search", new("Search a recipe or a material", "搜索配方或材料")),
        ("recipes", new("Recipes", "配方")),
        ("outOfStamina", new("Out of stamina: crafts that take stamina cannot start.", "精力不足：需要精力的制作无法开始。")),
        ("keepInStock", new("Keep in stock", "保持库存")),
        ("keepInStockAside", new("topped up when you open a workbench", "打开工作台时补充")),
        ("craftQueue", new("Craft queue", "制作队列")),
        ("materials", new("Materials for the whole queue", "整个队列所需材料")),
        ("materialsAside", new("need / you have", "需要 / 拥有")),
        ("madeOnTheWay", new("Made on the way", "途中制作")),
        ("countOf", new("{0} of {1}", "{0} / {1}")),
        ("noMatch", new("No recipes match \"{0}\".", "没有符合「{0}」的配方。")),
        ("noRecipes", new("No recipes in this view.", "这个视图里没有配方。")),
        ("allGroups", new("All groups", "全部分组")),
        ("canMakeCount", new("{0} / {1} can make", "{0} / {1} 可制作")),
        ("staminaCost", new("{0} stamina", "精力 {0}")),
        ("add", new("Add", "添加")),
        ("addTip", new("Add this many crafts to the queue", "把这么多次制作加入队列")),
        ("keep", new("Keep", "保持")),
        ("keepN", new("Keep {0}", "保持 {0}")),
        ("keepTip", new("Keep {0} of this in your home; opening any workbench queues the crafts to top it back up", "在家里保持 {0} 个；打开任意工作台时会把补充所需的制作加入队列")),
        ("keepStopTip", new("Stop keeping this in stock", "不再保持这个的库存")),
        ("queueEmptyHint", new("The queue is empty. Use Add on any recipe.", "队列是空的。在任意配方上点「添加」。")),
        ("idle", new("Idle", "空闲")),
        ("crafting", new("Crafting", "制作中")),
        ("paused", new("Paused", "已暂停")),
        ("stamina", new("Stamina", "精力")),
        ("staminaHave", new("Stamina {0}", "精力 {0}")),
        ("staminaOut", new("{0} (out)", "{0}（不足）")),
        ("dragTip", new("Drag to change the order", "拖动可调整顺序")),
        ("ready", new("Ready", "就绪")),
        ("moveUp", new("Move up", "上移")),
        ("moveDown", new("Move down", "下移")),
        ("removeTip", new("Remove from the queue", "从队列移除")),
        ("craftOne", new("{0} craft", "{0} 次制作")),
        ("craftMany", new("{0} crafts", "{0} 次制作")),
        ("orderOne", new("{0} order", "{0} 个订单")),
        ("orderMany", new("{0} orders", "{0} 个订单")),
        ("listSep", new(", ", "，")),
        ("itemSep", new(", ", "、")),
        ("workbench", new("Workbench", "工作台")),
        ("topUp", new("Top-up", "补充")),
        ("makes", new(", makes {0}", "，产出 {0}")),
        ("noMaterials", new("No materials to show.", "没有要显示的材料。")),
        ("nothingQueued", new("Nothing queued.", "队列里没有东西。")),
        ("shortN", new("short {0}", "缺 {0}")),
        ("enough", new("ok", "足够")),
        ("noParts", new("No parts need making.", "不需要制作中间材料。")),
        ("craftingNow", new("Crafting...", "制作中…")),
        ("stop", new("Stop", "停止")),
        ("stopTip", new("Stop after nothing more is started; the queue is kept", "不再开始新的制作后停止；队列会保留")),
        ("clear", new("Clear", "清空")),
        ("clearTip", new("Empty the queue", "清空队列")),
        ("queueIsEmpty", new("Queue is empty", "队列是空的")),
        ("craftQueueTip", new("Craft everything in the queue, making intermediates as needed", "制作队列里的所有物品，需要时先做中间材料")),
        ("nothingKept", new("Nothing kept in stock. Use Keep on any recipe.", "还没有保持库存的物品。在任意配方上点「保持」。")),
        ("unkeepTip", new("Stop keeping {0} in stock", "不再保持{0}的库存")),
        ("craftChain", new("Craft with intermediates", "连同中间材料一起制作")),
        ("craftChainTip", new("Make the missing intermediates first, then this, using the amount set on its recipe card", "先做缺少的中间材料，再按配方卡上设定的数量制作这个")),
    };

    public static IReadOnlyList<Phrase> All { get; } = Collect();

    /// <summary>A list of names or amounts: "a, b" in English, "a、b" in Chinese.</summary>
    public static string Join(bool isChinese, IEnumerable<string> entries) => string.Join(isChinese ? "、" : ", ", entries);

    private static List<Phrase> Collect()
    {
        var all = typeof(Texts).GetFields().Where(field => field.FieldType == typeof(Phrase)).Select(field => (Phrase)field.GetValue(null)!).ToList();

        foreach (var (_, phrase) in Page) all.Add(phrase);

        return all;
    }
}
