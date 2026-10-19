using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CraftingHub.Core;

namespace CraftingHub.Adapters;

/// <summary>The settings the session reads; fixed for the game session.</summary>
internal sealed record HubSettings(int MaxDepth, bool ShowMarks, bool KeepStocked, bool AutoStartTopUps, bool ResumeOnReopen, bool ResumeAtOtherBenches);

/// <summary>What keep-stocked targets need from the game and the disk: home counts, the targets file, and which save is loaded.</summary>
internal sealed record KeepPorts(HomeStock Stock, ITargetStore Store, Func<string?> SaveKey);

/// <summary>
/// Wires the workbench, its page and the core together. Each tick: keep the page script in place, act on the page's
/// buttons, recompute the chain marks when the game rebuilt its recipe list, step the queue, top up keep-stocked
/// targets when they are due, and redraw the panel when anything it shows changed.
/// </summary>
internal sealed class HubSession
{
    private static readonly IReadOnlyDictionary<string, string> NoMarks = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, int> NoKeeps = new Dictionary<string, int>();
    private static readonly IReadOnlyDictionary<string, int> NoYields = new Dictionary<string, int>();
    private static readonly IReadOnlyList<string> NoRecipeKeys = Array.Empty<string>();

    private static bool IsChinese => GameLanguage.IsChinese();

    /// <summary>A chain-marks pass slower than this is logged even when the count did not change.</summary>
    private const double SlowMarksMs = 5;

    private readonly Workbench _bench;
    private readonly WorkbenchPage _page;
    private readonly KeepPorts _keep;
    private HubSettings _settings;
    private readonly Func<bool> _takesUnitsOffStacks;
    private readonly Action<string> _log;
    private readonly Action<string> _logFileOnly;
    private readonly CraftQueue _queue = new();
    private readonly QueueRunner _runner;
    private readonly TopUpTiming _topUps = new();
    private readonly KeepSlot _slot;
    private IReadOnlyDictionary<string, string> _marks = NoMarks;
    private IReadOnlyDictionary<string, int> _keepByKey = NoKeeps;
    private IReadOnlyDictionary<int, int>? _keepHave;
    private IReadOnlyDictionary<string, int> _yields = NoYields;
    private QueueOutlook _outlook = QueueOutlook.Empty;
    private string _outlookFor = "";
    private string _status = "";
    private bool _hasStoppedForStamina;
    private string _shownView = "";
    private string _pickedKey = "";
    private string _pickedName = "";
    private int _seenRefreshes = -1;
    private bool _wasOpen;
    private BenchRead? _read;
    private bool _hasRead;
    private int _loggedMarkCount = -1;
    private IReadOnlyList<string> _recipeKeys = NoRecipeKeys;

    public HubSession(Workbench bench, WorkbenchPage page, KeepPorts keep, HubSettings settings, Func<bool> takesUnitsOffStacks, Action<string> log, Action<string> logFileOnly)
    {
        _bench = bench;
        _page = page;
        _keep = keep;
        _settings = settings;
        _takesUnitsOffStacks = takesUnitsOffStacks;
        _log = log;
        _logFileOnly = logFileOnly;
        _runner = new QueueRunner(settings.ResumeOnReopen, settings.ResumeAtOtherBenches, GameLanguage.IsChinese);
        _slot = new KeepSlot(keep.Store);
    }

    /// <summary>Called on the main thread at start and after each change; the marks and the outlook are worked out again at the next tick.</summary>
    public void UseSettings(HubSettings settings)
    {
        _settings = settings;
        _runner.Use(settings.ResumeOnReopen, settings.ResumeAtOtherBenches);
        _seenRefreshes = -1;
        _outlookFor = "";
    }

    public void Tick(float now)
    {
        var isOpen = _bench.IsOpen;

        // One read of the bench serves the whole tick: marks, keeps, the plan, top-ups, the outlook and names.
        _hasRead = false;
        _read = null;
        _page.KeepInstalled(now);

        while (PageMessages.TryTake(out var command)) Handle(command);

        // A bench opened again may show the same refresh count as when it closed, so opening always recomputes.
        if (isOpen && !_wasOpen)
        {
            _seenRefreshes = -1;
            _outlookFor = "";
            LoadTargets();
        }

        _wasOpen = isOpen;

        if (isOpen && _bench.Refreshes != _seenRefreshes)
        {
            var timer = Stopwatch.StartNew();
            var read = ReadBench();

            _recipeKeys = read?.Book.All.Select(recipe => recipe.Key).ToList() ?? NoRecipeKeys;
            RefreshMarks(read, timer);
            RefreshKeeps(read);
        }

        if (_runner.IsRunning) Apply(_runner.Tick(_bench.View(), now, Next));

        if (_settings.KeepStocked && _topUps.IsDue(isOpen, now, IsIdle)) TopUp();

        if (isOpen) RefreshOutlook();

        Show();
    }

    private void Handle(PageCommand command)
    {
        switch (command)
        {
            case PageCommand.Add add:
                AddOrder(new NewOrder(add.Key, RecipeName(add.Key), add.Crafts));
                break;
            case PageCommand.CraftNow craftNow:
                CraftNow(craftNow.Key, craftNow.Crafts);
                break;
            case PageCommand.Start:
                StartQueue();
                break;
            case PageCommand.Stop:
                Apply(_runner.Stop(Texts.Stopped.Pick(IsChinese)));
                break;
            case PageCommand.Remove remove:
                RemoveOrder(remove.Id);
                break;
            case PageCommand.Clear:
                if (_runner.IsRunning) Apply(_runner.Stop(Texts.QueueCleared.Pick(IsChinese)));

                _queue.Clear();
                _hasStoppedForStamina = false;
                break;
            case PageCommand.Move move:
                // Finished crafts are credited by order id, so a running step still counts for its own order after a move.
                _queue.Move(move.Id, move.To);
                break;
            case PageCommand.Amount amount:
                _queue.SetCrafts(amount.Id, amount.Crafts);
                break;
            case PageCommand.Keep keep:
                SetKeep(keep.Key, keep.Count);
                break;
            case PageCommand.Unkeep unkeep:
                Unkeep(unkeep.ItemId);
                break;
            case PageCommand.Ready:
                _log("page script running");
                _shownView = "";
                break;
            case PageCommand.NoScript:
                _page.Install();
                break;
            case PageCommand.Diagnostic diagnostic:
                _log("page: " + diagnostic.Text);
                break;
            case PageCommand.Unknown unknown:
                _log("page sent something unrecognised: " + unknown.Text);
                break;
        }
    }

    private void StartQueue()
    {
        if (_queue.Head == null)
        {
            _status = Texts.QueueEmpty.Pick(IsChinese);

            return;
        }

        StartRun("Craft queue");
        _status = Texts.Running.Pick(IsChinese);
    }

    /// <summary>Logged, so two stops close together can be told apart from one stop logged twice.</summary>
    private void StartRun(string source)
    {
        if (_runner.IsRunning) return;

        _runner.Start();
        _hasStoppedForStamina = false;
        _logFileOnly("queue started: " + source);
    }

    /// <summary>
    /// The ticker gave up on the session after repeated errors; a run in progress stops and says where to look. True
    /// only when a run was stopped, so errors while idle leave the ticker ready to stop the next run.
    /// </summary>
    public bool StopForErrors()
    {
        if (!_runner.IsRunning) return false;

        Apply(_runner.Stop(Texts.RepeatedErrors.Pick(IsChinese)));
        Show();

        return true;
    }

    /// <summary>Queues the order with each intermediate it needs as its own order ahead of it.</summary>
    private void AddOrder(NewOrder order)
    {
        var orders = ReadBench() is { } read
            ? QueueForecast.Split(_queue.Entries, read.Book, read.Stock, _slot.Targets.All, _settings.MaxDepth, order)
            : new[] { order };

        foreach (var queued in orders) _queue.Add(queued.Key, queued.Name, queued.Crafts);

        _status = Texts.Queued.Say(IsChinese, order.Name, order.Crafts);
    }

    private void CraftNow(string key, int crafts)
    {
        var name = RecipeName(key);

        switch (_runner.CraftNowPlace)
        {
            case CraftNowPlace.Refused:
                _status = _settings.ResumeAtOtherBenches
                    ? Texts.RefusedHere.Pick(IsChinese)
                    : Texts.RefusedElsewhere.Pick(IsChinese);
                break;
            case CraftNowPlace.BehindHead:
                _queue.PushFront(key, name, crafts, behindHead: true);
                _status = Texts.MakingNext.Say(IsChinese, name, crafts);
                break;
            case CraftNowPlace.Front:
                _queue.PushFront(key, name, crafts);
                StartRun("craft now");
                _status = Texts.MakingNow.Say(IsChinese, name, crafts);
                break;
        }
    }

    /// <summary>
    /// The craft in progress counts against its own order, so removing that order stops the run first; any other order,
    /// the head included between steps, just leaves the queue.
    /// </summary>
    private void RemoveOrder(long orderId)
    {
        if (_runner.CraftingFor == orderId) Apply(_runner.Stop(Texts.OrderRemoved.Pick(IsChinese)));

        _queue.Remove(orderId);
    }

    private void Apply(RunnerCommand command)
    {
        switch (command)
        {
            case RunnerCommand.ClickRecipe click:
                _pickedKey = click.Key;
                _pickedName = RecipeName(click.Key);
                _page.ClickRecipe(click.Key);
                break;
            case RunnerCommand.Craft craft:
                _page.Craft(craft.Quantity);
                _status = Texts.Crafting.Say(IsChinese, _pickedName, craft.Quantity, ForHead());
                break;
            case RunnerCommand.ConfirmResult:
                _page.ConfirmResult();
                break;
            case RunnerCommand.StepDone done:
                if (done.IsTarget) _queue.Progress(done.OrderId, done.Crafts);

                _bench.NoteStepDone();
                _logFileOnly($"made {done.Step.Recipe.Name} ×{done.Crafts}{(done.IsTarget ? "" : " (intermediate)")}");
                break;
            case RunnerCommand.Stopped stopped:
                _status = stopped.Reason;
                _hasStoppedForStamina = stopped.IsOutOfStamina;
                _log(stopped.IsDone ? "queue finished" : "queue stopped: " + stopped.Reason);
                break;
            case RunnerCommand.Paused paused:
                _status = paused.Reason;
                _logFileOnly("queue paused: " + paused.Reason);
                break;
            case RunnerCommand.Resumed resumed:
                _status = resumed.Reason;
                _logFileOnly("queue resumed");
                break;
            case RunnerCommand.Notice notice:
                _status = notice.Text;
                _logFileOnly("queue: " + notice.Text);
                break;
        }
    }

    private string ForHead() => _queue.Head is { } head && head.Key != _pickedKey ? Texts.ForOrder.Say(IsChinese, head.Name) : "";

    private NextStep Next()
    {
        if (ReadBench() is not { } read) return new NextStep.Wait(Texts.CouldNotReadBench.Pick(IsChinese));

        return QueuePlanner.Next(_queue.Head, read.Book, read.Stock, _slot.Targets.All, _settings.MaxDepth, _bench.ItemName, IsChinese);
    }

    /// <summary>Topping up waits for the bench to be free: no run, no craft in progress, no result on screen.</summary>
    private bool IsIdle()
    {
        if (_runner.IsRunning) return false;

        var view = _bench.View();

        return view.IsOpen && !view.IsBusy && !view.HasResult;
    }

    /// <summary>
    /// Queues what each target is short of, counting what the queue already makes. The run starts by itself only when
    /// the queue held nothing else, so the player's own orders are never started for them.
    /// </summary>
    private void TopUp()
    {
        var targets = _slot.Targets.All;

        if (targets.Count == 0 || ReadBench() is not { } read) return;

        if (_keep.Stock.Count(targets.Select(target => target.ItemId)) is not { } held) return;

        var plan = TopUpPlanner.Plan(targets, read.Book, read.Stock, held, _queue.Entries, _settings.MaxDepth, IsChinese);
        var wasEmpty = _queue.Head == null;

        if (plan.IsEmpty) return;

        foreach (var order in plan.Orders) _queue.Add(order.Recipe.Key, order.Recipe.Name, order.Crafts, isTopUp: true);

        _status = TopUpPlanner.Describe(plan, IsChinese);
        _logFileOnly(TopUpPlanner.LogLine(plan, targets, held));

        if (_settings.AutoStartTopUps && wasEmpty && plan.Orders.Count > 0) StartRun("keep stocked");
    }

    private void SetKeep(string key, int count)
    {
        if (!_settings.KeepStocked) return;

        LoadTargets();

        if (ReadBench()?.Book.Find(key) is not { } recipe)
        {
            _status = Texts.NotListedRecipe.Pick(IsChinese);

            return;
        }

        var name = _bench.ItemName(recipe.ProductId);
        var edit = _slot.Set(new KeepTarget(recipe.ProductId, key, name, count));

        _status = Describe(edit, count > 0 ? Texts.Keeping.Say(IsChinese, count, name) : Texts.NoLongerKeeping.Say(IsChinese, name));
        RefreshKeeps();

        if (count > 0 && edit is KeepEdit.Saved or KeepEdit.NotSaved) _topUps.Request();
    }

    private void Unkeep(int itemId)
    {
        if (!_settings.KeepStocked) return;

        LoadTargets();

        if (_slot.Targets.All.FirstOrDefault(target => target.ItemId == itemId) is not { Count: > 0 } target) return;

        _status = Describe(_slot.Remove(itemId), Texts.NoLongerKeeping.Say(IsChinese, target.Name));
        RefreshKeeps();
    }

    /// <summary>The status line after a target change: the change itself, or why it was not made or not saved.</summary>
    private static string Describe(KeepEdit edit, string change) => edit switch
    {
        KeepEdit.NoSave => Texts.NoSaveLoaded.Pick(IsChinese),
        KeepEdit.Unreadable => Texts.TargetsUnreadable.Pick(IsChinese),
        KeepEdit.NotSaved => Texts.NotSaved.Say(IsChinese, change),
        _ => change,
    };

    /// <summary>
    /// Targets belong to a save, so they are reread whenever another save is loaded. The queue belongs to its save too:
    /// left in place, its orders would count toward the next save's targets and keep its top-ups from starting.
    /// </summary>
    private void LoadTargets()
    {
        if (_slot.Follow(_keep.SaveKey())) ForgetQueue();

        if (!_settings.KeepStocked) return;

        var load = _slot.Load();

        if (load == SlotLoad.AlreadyRead) return;

        RefreshKeeps();

        if (load == SlotLoad.Read) _logFileOnly($"keep-stocked targets for {_slot.Save}: {_slot.Targets.All.Count}");

        if (load == SlotLoad.Unreadable) _status = Texts.TargetsFileUnreadable.Pick(IsChinese);
    }

    private void ForgetQueue()
    {
        if (_runner.IsRunning) Apply(_runner.Stop(Texts.AnotherSave.Pick(IsChinese)));

        _queue.Clear();
        _hasStoppedForStamina = false;
        _logFileOnly("another save was loaded; the queue was emptied");
    }

    private void RefreshKeeps() => RefreshKeeps(ReadBench());

    private void RefreshKeeps(BenchRead? read)
    {
        var targets = _slot.Targets.All;

        _keepByKey = read != null ? _slot.Targets.ByRecipe(read.Book) : NoKeeps;
        _keepHave = targets.Count > 0 ? _keep.Stock.Count(targets.Select(target => target.ItemId)) : null;
    }

    /// <summary>
    /// Costs the whole queue for the page. Reading the bench is not cheap, so it is redone only when the queue, the
    /// targets or the game's recipe list changed; a closed bench keeps the last costing.
    /// </summary>
    private void RefreshOutlook()
    {
        var costedFor = $"{IsChinese}|{_bench.Refreshes}|{string.Join(",", _queue.Entries.Select(entry => $"{entry.Id}:{entry.Crafts}"))}|{string.Join(",", _slot.Targets.All.Select(target => $"{target.ItemId}:{target.Count}"))}";

        if (costedFor == _outlookFor || ReadBench() is not { } read) return;

        _outlookFor = costedFor;
        _outlook = QueueForecast.Plan(_queue.Entries, read.Book, read.Stock, _slot.Targets.All, _settings.MaxDepth, _bench.ItemName, IsChinese);
        _yields = read.Book.All.Where(recipe => recipe.Yield > 1).ToDictionary(recipe => recipe.Key, recipe => recipe.Yield);
    }

    /// <summary>The timer started before the bench was read, so a slow read shows in the logged time too.</summary>
    private void RefreshMarks(BenchRead? read, Stopwatch timer)
    {
        _seenRefreshes = _bench.Refreshes;

        if (!_settings.ShowMarks || _settings.MaxDepth <= 0)
        {
            _marks = NoMarks;

            return;
        }

        _marks = read == null ? NoMarks : ChainMarks.Find(read.Book, read.Stock, read.CraftableNow, _settings.MaxDepth, IsChinese);

        var elapsedMs = timer.Elapsed.TotalMilliseconds;

        if (_marks.Count == _loggedMarkCount && elapsedMs <= SlowMarksMs) return;

        _loggedMarkCount = _marks.Count;
        _logFileOnly($"chain marks: {_marks.Count} recipes in {elapsedMs:0.#} ms");
    }

    /// <summary>The order the progress bar goes on: the one the step in progress counts for, or between steps the head, which is planned next.</summary>
    private long ActiveOrderId() => _runner.CraftingFor ?? _queue.Head?.Id ?? 0;

    private string RecipeName(string key) => ReadBench()?.Book.Find(key)?.Name ?? key;

    /// <summary>The bench read once per tick and shared; null when it cannot be read.</summary>
    private BenchRead? ReadBench()
    {
        if (_hasRead) return _read;

        _read = _bench.Read();
        _hasRead = true;

        return _read;
    }

    private void Show()
    {
        var keep = new KeepView(_settings.KeepStocked, _slot.Targets.All, _keepByKey, _keepHave);
        var stamina = StaminaView.From(_bench.Stamina(), _recipeKeys, _queue.Head != null, _hasStoppedForStamina);
        var view = HubView.Build(_marks, _queue.Entries, _runner.IsRunning, _status, keep, _runner.IsPaused, _outlook, _yields, ActiveOrderId(), stamina, _takesUnitsOffStacks());

        if (view == _shownView) return;

        _shownView = view;
        _page.Show(view);
    }
}
