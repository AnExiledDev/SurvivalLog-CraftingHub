using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CraftingHub.Core;

namespace CraftingHub.Adapters;

/// <summary>The settings the session reads; fixed for the game session.</summary>
internal sealed record HubSettings(int MaxDepth, bool ShowMarks, bool KeepStocked, bool AutoStartTopUps, bool ResumeOnReopen);

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

    private readonly Workbench _bench;
    private readonly WorkbenchPage _page;
    private readonly KeepPorts _keep;
    private readonly HubSettings _settings;
    private readonly Action<string> _log;
    private readonly Action<string> _logFileOnly;
    private readonly CraftQueue _queue = new();
    private readonly QueueRunner _runner;
    private readonly TopUpTiming _topUps = new();
    private readonly KeepSlot _slot;
    private IReadOnlyDictionary<string, string> _marks = NoMarks;
    private IReadOnlyDictionary<string, int> _keepByKey = NoKeeps;
    private string _status = "";
    private string _shownView = "";
    private string _pickedKey = "";
    private string _pickedName = "";
    private int _seenRefreshes = -1;
    private bool _wasOpen;

    public HubSession(Workbench bench, WorkbenchPage page, KeepPorts keep, HubSettings settings, Action<string> log, Action<string> logFileOnly)
    {
        _bench = bench;
        _page = page;
        _keep = keep;
        _settings = settings;
        _log = log;
        _logFileOnly = logFileOnly;
        _runner = new QueueRunner(settings.ResumeOnReopen);
        _slot = new KeepSlot(keep.Store);
    }

    public void Tick(float now)
    {
        var isOpen = _bench.IsOpen;

        _page.KeepInstalled(now);

        while (PageMessages.TryTake(out var command)) Handle(command);

        // A bench opened again may show the same refresh count as when it closed, so opening always recomputes.
        if (isOpen && !_wasOpen)
        {
            _seenRefreshes = -1;
            LoadTargets();
        }

        _wasOpen = isOpen;

        if (isOpen && _bench.Refreshes != _seenRefreshes)
        {
            RefreshMarks();
            RefreshKeeps();
        }

        if (_runner.IsRunning) Apply(_runner.Tick(_bench.View(), now, Next));

        if (_settings.KeepStocked && _topUps.IsDue(isOpen, now, IsIdle)) TopUp();

        Show();
    }

    private void Handle(PageCommand command)
    {
        switch (command)
        {
            case PageCommand.Add add:
                _queue.Add(add.Key, RecipeName(add.Key), add.Crafts);
                _status = $"Queued {RecipeName(add.Key)} ×{add.Crafts}";
                break;
            case PageCommand.CraftNow craftNow:
                _queue.PushFront(craftNow.Key, RecipeName(craftNow.Key), craftNow.Crafts);
                _runner.Start();
                _status = _runner.IsPaused ? $"Queued {RecipeName(craftNow.Key)} x{craftNow.Crafts} first; the queue carries on at the workbench it was running on" : $"Making {RecipeName(craftNow.Key)} ×{craftNow.Crafts} with its intermediates";
                break;
            case PageCommand.Start:
                StartQueue();
                break;
            case PageCommand.Stop:
                Apply(_runner.Stop("stopped"));
                break;
            case PageCommand.Remove remove:
                RemoveOrder(remove.Index);
                break;
            case PageCommand.Clear:
                if (_runner.IsRunning) Apply(_runner.Stop("queue cleared"));

                _queue.Clear();
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
            _status = "the queue is empty";

            return;
        }

        _runner.Start();
        _status = "running the queue";
    }

    /// <summary>The craft in progress counts against the head order, so removing the head stops the run first.</summary>
    private void RemoveOrder(int index)
    {
        if (index == 0 && _runner.IsRunning) Apply(_runner.Stop("stopped: the running order was removed"));

        _queue.Remove(index);
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
                _status = $"Crafting {_pickedName} ×{craft.Quantity}{ForHead()}";
                break;
            case RunnerCommand.ConfirmResult:
                _page.ConfirmResult();
                break;
            case RunnerCommand.StepDone done:
                if (done.IsTarget) _queue.Progress(done.Crafts);

                _logFileOnly($"made {done.Step.Recipe.Name} ×{done.Crafts}{(done.IsTarget ? "" : " (intermediate)")}");
                break;
            case RunnerCommand.Stopped stopped:
                _status = stopped.Reason;
                _log(stopped.IsDone ? "queue finished" : "queue stopped: " + stopped.Reason);
                break;
            case RunnerCommand.Paused paused:
                _status = paused.Reason;
                _logFileOnly("queue paused: " + paused.Reason);
                break;
            case RunnerCommand.Resumed:
                _status = "the workbench is open again; carrying on with the queue";
                _logFileOnly("queue resumed");
                break;
        }
    }

    private string ForHead() => _queue.Head is { } head && head.Key != _pickedKey ? $" for {head.Name}" : "";

    private NextStep Next()
    {
        if (_bench.Read() is not { } read) return new NextStep.Blocked("could not read the workbench");

        return QueuePlanner.Next(_queue.Head, read.Book, read.Stock, _slot.Targets.All, _settings.MaxDepth, _bench.ItemName);
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

        if (targets.Count == 0 || _bench.Read() is not { } read) return;

        if (_keep.Stock.Count(targets.Select(target => target.ItemId)) is not { } held) return;

        var plan = TopUpPlanner.Plan(targets, read.Book, read.Stock, held, _queue.Entries, _settings.MaxDepth);
        var wasEmpty = _queue.Head == null;

        if (plan.IsEmpty) return;

        foreach (var order in plan.Orders) _queue.Add(order.Recipe.Key, order.Recipe.Name, order.Crafts, isTopUp: true);

        _status = TopUpPlanner.Describe(plan);
        _logFileOnly(_status);

        if (_settings.AutoStartTopUps && wasEmpty && plan.Orders.Count > 0) _runner.Start();
    }

    private void SetKeep(string key, int count)
    {
        if (!_settings.KeepStocked) return;

        LoadTargets();

        if (_bench.Read()?.Book.Find(key) is not { } recipe)
        {
            _status = "that recipe is not at this workbench";

            return;
        }

        var name = _bench.ItemName(recipe.ProductId);
        var edit = _slot.Set(new KeepTarget(recipe.ProductId, key, name, count));

        _status = Describe(edit, count > 0 ? $"Keeping {count} {name} in stock" : $"No longer keeping {name} in stock");
        RefreshKeeps();

        if (count > 0 && edit is KeepEdit.Saved or KeepEdit.NotSaved) _topUps.Request();
    }

    private void Unkeep(int itemId)
    {
        if (!_settings.KeepStocked) return;

        LoadTargets();

        if (_slot.Targets.All.FirstOrDefault(target => target.ItemId == itemId) is not { Count: > 0 } target) return;

        _status = Describe(_slot.Remove(itemId), $"No longer keeping {target.Name} in stock");
        RefreshKeeps();
    }

    /// <summary>The status line after a target change: the change itself, or why it was not made or not saved.</summary>
    private static string Describe(KeepEdit edit, string change) => edit switch
    {
        KeepEdit.NoSave => "no save is loaded, so the target was not kept",
        KeepEdit.Unreadable => "the targets file could not be read, so nothing was changed (see the log)",
        KeepEdit.NotSaved => change + " (could not save it to the targets file, see the log)",
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

        if (load == SlotLoad.Unreadable) _status = "the keep-stocked targets file could not be read (see the log)";
    }

    private void ForgetQueue()
    {
        if (_runner.IsRunning) Apply(_runner.Stop("another save was loaded"));

        _queue.Clear();
        _logFileOnly("another save was loaded; the queue was emptied");
    }

    private void RefreshKeeps() => _keepByKey = _bench.Read() is { } read ? _slot.Targets.ByRecipe(read.Book) : NoKeeps;

    private void RefreshMarks()
    {
        _seenRefreshes = _bench.Refreshes;

        if (!_settings.ShowMarks || _settings.MaxDepth <= 0)
        {
            _marks = NoMarks;

            return;
        }

        var timer = Stopwatch.StartNew();
        var read = _bench.Read();

        _marks = read == null ? NoMarks : ChainMarks.Find(read.Book, read.Stock, read.CraftableNow, _settings.MaxDepth);
        _logFileOnly($"chain marks: {_marks.Count} recipes in {timer.Elapsed.TotalMilliseconds:0.#} ms");
    }

    private string RecipeName(string key) => _bench.Read()?.Book.Find(key)?.Name ?? key;

    private void Show()
    {
        var keep = new KeepView(_settings.KeepStocked, _slot.Targets.All, _keepByKey);
        var view = HubView.Build(_marks, _queue.Entries, _runner.IsRunning, _status, keep);

        if (view == _shownView) return;

        _shownView = view;
        _page.Show(view);
    }
}
