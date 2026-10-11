using System;
using System.Collections.Generic;
using System.Diagnostics;
using CraftingHub.Core;

namespace CraftingHub.Adapters;

/// <summary>The settings the session reads; fixed for the game session.</summary>
internal sealed record HubSettings(int MaxDepth, bool ShowMarks);

/// <summary>
/// Wires the workbench, its page and the core together. Each tick: keep the page script in place, act on the page's
/// buttons, recompute the chain marks when the game rebuilt its recipe list, step the queue, and redraw the panel
/// when anything it shows changed.
/// </summary>
internal sealed class HubSession
{
    private static readonly IReadOnlyDictionary<string, string> NoMarks = new Dictionary<string, string>();

    private readonly Workbench _bench;
    private readonly WorkbenchPage _page;
    private readonly HubSettings _settings;
    private readonly Action<string> _log;
    private readonly Action<string> _logFileOnly;
    private readonly CraftQueue _queue = new();
    private readonly QueueRunner _runner = new();
    private IReadOnlyDictionary<string, string> _marks = NoMarks;
    private string _status = "";
    private string _shownView = "";
    private string _pickedKey = "";
    private string _pickedName = "";
    private int _seenRefreshes = -1;
    private bool _wasOpen;

    public HubSession(Workbench bench, WorkbenchPage page, HubSettings settings, Action<string> log, Action<string> logFileOnly)
    {
        _bench = bench;
        _page = page;
        _settings = settings;
        _log = log;
        _logFileOnly = logFileOnly;
    }

    public void Tick(float now)
    {
        var isOpen = _bench.IsOpen;

        _page.KeepInstalled(now);

        while (PageMessages.TryTake(out var command)) Handle(command);

        // A bench opened again may show the same refresh count as when it closed, so opening always recomputes.
        if (isOpen && !_wasOpen) _seenRefreshes = -1;

        _wasOpen = isOpen;

        if (isOpen && _bench.Refreshes != _seenRefreshes) RefreshMarks();

        if (_runner.IsRunning) Apply(_runner.Tick(_bench.View(), now, Next));

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
                _status = $"Making {RecipeName(craftNow.Key)} ×{craftNow.Crafts} with its intermediates";
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
        }
    }

    private string ForHead() => _queue.Head is { } head && head.Key != _pickedKey ? $" for {head.Name}" : "";

    private NextStep Next()
    {
        if (_bench.Read() is not { } read) return new NextStep.Blocked("could not read the workbench");

        return QueuePlanner.Next(_queue.Head, read.Book, read.Stock, _settings.MaxDepth, _bench.ItemName);
    }

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
        var view = HubView.Build(_marks, _queue.Entries, _runner.IsRunning, _status);

        if (view == _shownView) return;

        _shownView = view;
        _page.Show(view);
    }
}
