using System;
using System.Collections.Generic;

namespace CraftingHub.Core;

/// <summary>What the runner can see of the open workbench this frame.</summary>
/// <param name="IsBusy">A craft or a batch of crafts is in progress.</param>
/// <param name="HasResult">The result screen is showing.</param>
/// <param name="MaxCrafts">How many crafts the game allows with what is laid out.</param>
/// <param name="Contents">Item id to count, of what sits on the workbench.</param>
/// <param name="BatchDone">Crafts finished in the current batch, as the game counts them.</param>
public readonly record struct BenchView(bool IsOpen, bool IsBusy, bool HasResult, int MaxCrafts, IReadOnlyDictionary<int, int> Contents, int BatchDone);

/// <summary>What to craft next, worked out fresh from the queue and the stock each time it is asked.</summary>
public abstract record NextStep
{
    private NextStep()
    {
    }

    public sealed record Craft(CraftStep Step, bool IsTarget) : NextStep;

    public sealed record Finished : NextStep;

    public sealed record Blocked(string Reason) : NextStep;
}

/// <summary>What the runner wants done this frame. Every action is one the player could take by clicking.</summary>
public abstract record RunnerCommand
{
    private RunnerCommand()
    {
    }

    public sealed record None : RunnerCommand;

    public sealed record ClickRecipe(string Key) : RunnerCommand;

    public sealed record Craft(int Quantity) : RunnerCommand;

    public sealed record ConfirmResult : RunnerCommand;

    /// <summary>A step finished; <see cref="Crafts"/> counts failed attempts too, since the game spent the materials.</summary>
    public sealed record StepDone(CraftStep Step, int Crafts, bool IsTarget) : RunnerCommand;

    public sealed record Stopped(string Reason, bool IsDone) : RunnerCommand;
}

/// <summary>
/// Drives the workbench through the clicks a player makes: pick a recipe (the game lays out the materials), craft,
/// confirm the result, repeat. One step at a time and re-planned before every step, so a failed craft or a short
/// batch is planned around rather than tracked. Every wait has a timeout, so a game that does not respond stops the
/// run with a reason instead of leaving it stuck.
/// </summary>
public sealed class QueueRunner
{
    public const double SettleSeconds = 0.25;
    public const double FillTimeoutSeconds = 3;
    public const double StartTimeoutSeconds = 4;
    public const double QuietDoneSeconds = 1.5;
    public const double CollectTimeoutSeconds = 4;
    public const int MaxIntermediateBatches = 30;

    private enum Stage
    {
        Idle,
        Ready,
        Filling,
        Crafting,
        Collecting,
    }

    private Stage _stage = Stage.Idle;
    private NextStep.Craft? _current;
    private int _sentQuantity;
    private int _batchDone;
    private bool _hasStarted;
    private double _stageSince;
    private double? _settledSince;
    private double? _quietSince;
    private int _intermediateBatches;

    public bool IsRunning => _stage != Stage.Idle;

    public void Start()
    {
        if (IsRunning) return;

        _stage = Stage.Ready;
        _intermediateBatches = 0;
    }

    public RunnerCommand Stop(string reason, bool isDone = false)
    {
        _stage = Stage.Idle;
        _current = null;

        return new RunnerCommand.Stopped(reason, isDone);
    }

    public RunnerCommand Tick(BenchView bench, double now, Func<NextStep> next)
    {
        if (_stage == Stage.Idle) return new RunnerCommand.None();

        if (!bench.IsOpen) return Stop("the workbench closed; the queue is kept, press Start to carry on");

        return _stage switch
        {
            Stage.Ready => PickNext(bench, now, next),
            Stage.Filling => CraftWhenLaidOut(bench, now),
            Stage.Crafting => WaitForResult(bench, now),
            Stage.Collecting => WaitForCollect(bench, now),
            _ => new RunnerCommand.None(),
        };
    }

    /// <summary>The bench holds exactly the recipe's kinds of material, with enough of each for one craft.</summary>
    public static bool HoldsOneCraft(IReadOnlyDictionary<int, int> contents, IReadOnlyDictionary<int, int> materials)
    {
        if (contents.Count != materials.Count) return false;

        foreach (var (itemId, perCraft) in materials)
        {
            if (!contents.TryGetValue(itemId, out var count) || count < perCraft) return false;
        }

        return true;
    }

    private RunnerCommand PickNext(BenchView bench, double now, Func<NextStep> next)
    {
        if (bench.IsBusy || bench.HasResult) return new RunnerCommand.None();

        switch (next())
        {
            case NextStep.Finished:
                return Stop("queue finished", isDone: true);
            case NextStep.Blocked blocked:
                return Stop(blocked.Reason);
            case NextStep.Craft craft:
                // Intermediates that never lead to a finished target mean the plan and the game disagree; better to
                // stop than to turn every log in the house into planks.
                if (!craft.IsTarget && ++_intermediateBatches > MaxIntermediateBatches) return Stop($"stopped after {MaxIntermediateBatches} intermediate crafts without finishing the target");

                _current = craft;
                _settledSince = null;
                Enter(Stage.Filling, now);

                return new RunnerCommand.ClickRecipe(craft.Step.Recipe.Key);
            default:
                return Stop("nothing to craft");
        }
    }

    private RunnerCommand CraftWhenLaidOut(BenchView bench, double now)
    {
        var step = Current.Step;

        if (bench.IsBusy || bench.MaxCrafts < 1 || !HoldsOneCraft(bench.Contents, step.Recipe.Materials))
        {
            _settledSince = null;

            return now - _stageSince > FillTimeoutSeconds ? Stop($"the game did not lay out the materials for {step.Recipe.Name}") : new RunnerCommand.None();
        }

        // The game fills the bench over a frame or two and then recounts what it allows; let that settle.
        _settledSince ??= now;

        if (now - _settledSince < SettleSeconds) return new RunnerCommand.None();

        _sentQuantity = Math.Min(step.Crafts, bench.MaxCrafts);
        _batchDone = 0;
        _hasStarted = false;
        _quietSince = null;
        Enter(Stage.Crafting, now);

        return new RunnerCommand.Craft(_sentQuantity);
    }

    private RunnerCommand WaitForResult(BenchView bench, double now)
    {
        var isActive = bench.IsBusy || bench.HasResult;

        _batchDone = Math.Max(_batchDone, bench.BatchDone);

        if (isActive) _hasStarted = true;

        if (!_hasStarted) return now - _stageSince > StartTimeoutSeconds ? Stop($"the craft of {Current.Step.Recipe.Name} did not start") : new RunnerCommand.None();

        if (bench.HasResult && !bench.IsBusy)
        {
            Enter(Stage.Collecting, now);

            return new RunnerCommand.ConfirmResult();
        }

        if (isActive)
        {
            _quietSince = null;

            return new RunnerCommand.None();
        }

        // Started, and now neither crafting nor showing a result: the game finished without a result screen.
        _quietSince ??= now;

        return now - _quietSince >= QuietDoneSeconds ? FinishStep() : new RunnerCommand.None();
    }

    private RunnerCommand WaitForCollect(BenchView bench, double now)
    {
        _batchDone = Math.Max(_batchDone, bench.BatchDone);

        if (!bench.HasResult) return FinishStep();

        return now - _stageSince > CollectTimeoutSeconds ? Stop("the result screen did not close") : new RunnerCommand.None();
    }

    private RunnerCommand FinishStep()
    {
        var finished = Current;
        var crafts = _batchDone > 0 ? Math.Min(_batchDone, _sentQuantity) : _sentQuantity;

        if (finished.IsTarget) _intermediateBatches = 0;

        _current = null;
        _stage = Stage.Ready;

        return new RunnerCommand.StepDone(finished.Step, crafts, finished.IsTarget);
    }

    private NextStep.Craft Current => _current ?? throw new InvalidOperationException("no step in progress");

    private void Enter(Stage stage, double now)
    {
        _stage = stage;
        _stageSince = now;
    }
}
