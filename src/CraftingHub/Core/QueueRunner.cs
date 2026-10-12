using System;
using System.Collections.Generic;

namespace CraftingHub.Core;

/// <summary>What the runner can see of the open workbench this frame.</summary>
/// <param name="IsBusy">A craft or a batch of crafts is in progress.</param>
/// <param name="HasResult">The result screen is showing.</param>
/// <param name="MaxCrafts">How many crafts the game allows with what is laid out.</param>
/// <param name="Contents">Item id to count, of what sits on the workbench.</param>
/// <param name="BatchDone">
/// Crafts finished in the game's batch count. The game zeroes it only when a batch of several crafts starts, so
/// outside one it still holds the last batch's count. Reported from the last known state even once the window closed.
/// </param>
/// <param name="BenchId">
/// Which workbench is open (the placed furniture, not its kind), so a paused run can be kept to its own when other
/// workbenches are off; 0 when unknown.
/// </param>
/// <param name="BatchActive">
/// The game's batch flag. Set when a batch of several crafts starts, which also zeroes <see cref="BatchDone"/> and
/// <see cref="BatchSuccess"/>. Not expected to show with nothing crafting, though the runner copes if it ever does.
/// </param>
/// <param name="BatchSuccess">The crafts in <see cref="BatchDone"/> that succeeded; reset with it.</param>
/// <param name="IsCrafting">A craft is being made right now, as opposed to a batch flag left set.</param>
public readonly record struct BenchView(bool IsOpen, bool IsBusy, bool HasResult, int MaxCrafts, IReadOnlyDictionary<int, int> Contents, int BatchDone, long BenchId = 0, bool BatchActive = false, int BatchSuccess = 0, bool IsCrafting = false);

/// <summary>What to craft next, worked out fresh from the queue and the stock each time it is asked.</summary>
public abstract record NextStep
{
    private NextStep()
    {
    }

    /// <param name="OrderId">The queue order this step was planned for, so its crafts are credited to that order.</param>
    public sealed record Craft(CraftStep Step, bool IsTarget, long OrderId = 0) : NextStep;

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
    public sealed record StepDone(CraftStep Step, int Crafts, bool IsTarget, long OrderId = 0) : RunnerCommand;

    public sealed record Stopped(string Reason, bool IsDone) : RunnerCommand;

    /// <summary>The run waits for its workbench to open again; the queue is untouched.</summary>
    public sealed record Paused(string Reason) : RunnerCommand;

    public sealed record Resumed(string Reason) : RunnerCommand;

    /// <summary>Something the player should know while the run carries on; only the status line changes.</summary>
    public sealed record Notice(string Text) : RunnerCommand;
}

/// <summary>Where a "craft with intermediates" order may go right now.</summary>
public enum CraftNowPlace
{
    /// <summary>The front of the queue; the run starts with it.</summary>
    Front,

    /// <summary>Second, behind the order whose step is being made, so that step is still credited to it.</summary>
    BehindHead,

    /// <summary>Not taken: a paused run holds the head of the queue until it carries on.</summary>
    Refused,
}

/// <summary>
/// Drives the workbench through the clicks a player makes: pick a recipe (the game lays out the materials), craft,
/// confirm the result, repeat. One step at a time and re-planned before every step, so a failed craft or a short
/// batch is planned around rather than tracked. Every wait has a timeout, so a game that does not respond stops the
/// run with a reason instead of leaving it stuck. The game cancels a craft whose window closes, so a closed workbench
/// pauses the run (or stops it, when resuming is off) and the run carries on once a workbench is open again: any
/// workbench, or only the one it was running on when other workbenches are off. Every workbench lists the same
/// recipes (the game builds the list from the whole recipe table, filtered by the player's level and unlocks, never by
/// which workbench is open), so any of them can make the next step.
/// </summary>
public sealed class QueueRunner
{
    public const double SettleSeconds = 0.25;
    public const double FillTimeoutSeconds = 3;
    public const double StartTimeoutSeconds = 4;
    public const double QuietDoneSeconds = 1.5;
    public const double CollectTimeoutSeconds = 4;
    public const int MaxIntermediateBatches = 30;
    public const double ResumeSettleSeconds = 1;

    /// <summary>A batch flag with nothing crafting for this long is taken as stale rather than a batch between crafts.</summary>
    public const double StaleBatchSeconds = 3;

    /// <summary>A workbench that stays busy and unchanged this long gets a status line, so a stall is not silent.</summary>
    public const double BusyNoticeSeconds = 30;

    private const string ClosedReason = "the workbench closed; the queue is kept, press Craft queue to carry on";
    private const string PausedReason = "the workbench closed; the queue carries on when you open it again";
    private const string PausedAnyBenchReason = "the workbench closed; the queue carries on when you open any workbench";
    private const string ResumedReason = "the workbench is open again; carrying on with the queue";
    private const string ResumedAnyBenchReason = "a workbench is open; carrying on with the queue here";
    private const string ElsewhereReason = "the queue carries on at the workbench it was running on; press Stop, then Craft queue, to run it here";
    private const string StaleBatchNotice = "the workbench shows a batch in progress with nothing being made; carrying on with the next craft";
    private const string BusyNotice = "waiting for the workbench: it has shown a craft in progress for a while with no change";

    private enum Stage
    {
        Idle,
        Ready,
        Filling,
        Crafting,
        Collecting,
        Paused,
        Resuming,
    }

    private readonly bool _resumesOnReopen;
    private readonly bool _resumesAtOtherBenches;

    private Stage _stage = Stage.Idle;
    private NextStep.Craft? _current;
    private int _sentQuantity;
    private int _batchDone;
    private int _batchSuccess;
    private bool _hasSeenBatch;
    private bool _isBatchStale;
    private (bool IsCrafting, bool BatchActive, bool HasResult, int BatchDone)? _busyWith;
    private double _busySince;
    private bool _hasToldBusy;
    private bool _hasStarted;
    private double _stageSince;
    private double? _settledSince;
    private double? _quietSince;
    private int _intermediateBatches;
    private long _benchId;
    private bool _hasToldElsewhere;

    /// <param name="resumesOnReopen">Off, a closed workbench stops the run, as it did before runs could pause.</param>
    /// <param name="resumesAtOtherBenches">
    /// On, a paused run carries on at whichever workbench opens next. Off, only at the one it was running on.
    /// </param>
    public QueueRunner(bool resumesOnReopen = true, bool resumesAtOtherBenches = false)
    {
        _resumesOnReopen = resumesOnReopen;
        _resumesAtOtherBenches = resumesAtOtherBenches;
    }

    /// <summary>A run is in progress, paused ones included: they still hold the queue's head and wait to carry on.</summary>
    public bool IsRunning => _stage != Stage.Idle;

    public bool IsPaused => _stage is Stage.Paused or Stage.Resuming;

    public CraftNowPlace CraftNowPlace => IsPaused ? CraftNowPlace.Refused : _current != null ? CraftNowPlace.BehindHead : CraftNowPlace.Front;

    /// <summary>The order the step in progress counts for, wherever it now sits in the queue; null between steps.</summary>
    public long? CraftingFor => _current?.OrderId;

    /// <summary>
    /// Starts a run. A paused run is left waiting to carry on; with other workbenches off, Stop, then Start, runs the
    /// queue at the workbench open now.
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        _stage = Stage.Ready;
        _benchId = 0;
        _intermediateBatches = 0;
        _isBatchStale = false;
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

        // The busy clock only runs while the run waits to pick its next step.
        if (_stage != Stage.Ready) _busyWith = null;

        if (IsPaused) return WaitForBench(bench, now);

        if (!bench.IsOpen) return Interrupted(bench);

        if (bench.BenchId != 0) _benchId = bench.BenchId;

        // A stale batch flag is cleared by the game at the next craft, or a real batch takes its place.
        if (!bench.BatchActive || bench.IsCrafting) _isBatchStale = false;

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
        if (IsBusy(bench) || bench.HasResult) return WaitWhileBusy(bench, now);

        _busyWith = null;

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

    /// <summary>
    /// The workbench is busy before the next step. A safety net: should a workbench ever report a batch that nothing
    /// is crafting for a few seconds, it is taken as stale and crafted past (the game resets a stale batch at the next
    /// craft). Anything else that sits unchanged for long is shown on the status line once and otherwise waited out.
    /// </summary>
    private RunnerCommand WaitWhileBusy(BenchView bench, double now)
    {
        var busyWith = (bench.IsCrafting, bench.BatchActive, bench.HasResult, bench.BatchDone);

        if (_busyWith != busyWith)
        {
            _busyWith = busyWith;
            _busySince = now;
            _hasToldBusy = false;
        }

        var isBatchIdle = bench.BatchActive && !bench.IsCrafting && !bench.HasResult;

        if (isBatchIdle && now - _busySince >= StaleBatchSeconds)
        {
            _isBatchStale = true;
            _busyWith = null;

            return new RunnerCommand.Notice(StaleBatchNotice);
        }

        if (_hasToldBusy || now - _busySince < BusyNoticeSeconds) return new RunnerCommand.None();

        _hasToldBusy = true;

        return new RunnerCommand.Notice(BusyNotice);
    }

    /// <summary>Busy as the game shows it, except that a batch flag taken as stale no longer counts.</summary>
    private bool IsBusy(BenchView bench) => _isBatchStale ? bench.IsCrafting : bench.IsBusy;

    private RunnerCommand CraftWhenLaidOut(BenchView bench, double now)
    {
        var step = Current.Step;

        if (IsBusy(bench) || bench.MaxCrafts < 1 || !HoldsOneCraft(bench.Contents, step.Recipe.Materials))
        {
            _settledSince = null;

            return now - _stageSince > FillTimeoutSeconds ? Stop($"the game did not lay out the materials for {step.Recipe.Name}") : new RunnerCommand.None();
        }

        // The game fills the bench over a frame or two and then recounts what it allows; let that settle.
        _settledSince ??= now;

        if (now - _settledSince < SettleSeconds) return new RunnerCommand.None();

        _sentQuantity = Math.Min(step.Crafts, bench.MaxCrafts);
        _batchDone = 0;
        _batchSuccess = 0;
        _hasSeenBatch = false;
        _hasStarted = false;
        _quietSince = null;
        Enter(Stage.Crafting, now);

        return new RunnerCommand.Craft(_sentQuantity);
    }

    private RunnerCommand WaitForResult(BenchView bench, double now)
    {
        var isActive = IsBusy(bench) || bench.HasResult;

        CountBatch(bench);

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
        CountBatch(bench);

        if (!bench.HasResult) return FinishStep();

        return now - _stageSince > CollectTimeoutSeconds ? Stop("the result screen did not close") : new RunnerCommand.None();
    }

    /// <summary>
    /// The game's batch count belongs to this step only once its batch is seen crafting: a single craft never resets
    /// the count and a batch zeroes it as it starts, so any count read before then is from an earlier batch (a stale
    /// batch flag with nothing crafting does not count as seeing it). Once begun, the count stays this step's after
    /// the batch ends too, which is when its last craft shows.
    /// </summary>
    private void CountBatch(BenchView bench)
    {
        if (_sentQuantity > 1 && bench.BatchActive && bench.IsCrafting) _hasSeenBatch = true;

        if (!_hasSeenBatch) return;

        _batchDone = Math.Max(_batchDone, bench.BatchDone);
        _batchSuccess = Math.Max(_batchSuccess, bench.BatchSuccess);
    }

    /// <summary>
    /// Read from the window just closed. The close may post its cancelled craft as a failed one, so only successes
    /// since the last look add to the count; a real failure in that moment is made again rather than risk counting
    /// the cancelled craft.
    /// </summary>
    private void CountLateSuccesses(BenchView closed)
    {
        if (!_hasSeenBatch) return;

        _batchDone += Math.Max(0, closed.BatchSuccess - _batchSuccess);
        _batchSuccess = Math.Max(_batchSuccess, closed.BatchSuccess);
    }

    /// <summary>
    /// The workbench closed under the run. Closing ends the craft in progress before it uses anything, so only crafts
    /// the game already finished count: a result on screen or a quiet finish means the whole step was made, otherwise
    /// the batch's finished crafts, plus any that succeeded in the moment before the close.
    /// Those are reported first; the next tick, still closed, pauses or stops the run.
    /// </summary>
    private RunnerCommand Interrupted(BenchView bench)
    {
        if (_stage is Stage.Crafting or Stage.Collecting) CountLateSuccesses(bench);

        var batchCrafts = Math.Min(_batchDone, _sentQuantity);

        if (_stage == Stage.Collecting || (_stage == Stage.Crafting && _quietSince != null)) return FinishStep();

        if (_stage == Stage.Crafting && batchCrafts > 0) return FinishStep(batchCrafts);

        _current = null;

        if (!_resumesOnReopen) return Stop(ClosedReason);

        _stage = Stage.Paused;
        _hasToldElsewhere = false;

        return new RunnerCommand.Paused(_resumesAtOtherBenches ? PausedAnyBenchReason : PausedReason);
    }

    /// <summary>
    /// Paused: carry on once a workbench has stayed open a moment, so the game has rebuilt its recipe list before the
    /// next step is planned. With other workbenches off, only the run's own workbench will do; another leaves the run
    /// waiting, said once per opening.
    /// </summary>
    private RunnerCommand WaitForBench(BenchView bench, double now)
    {
        if (!bench.IsOpen)
        {
            _stage = Stage.Paused;
            _hasToldElsewhere = false;

            return new RunnerCommand.None();
        }

        if (!_resumesAtOtherBenches && _benchId != 0 && bench.BenchId != _benchId) return WaitElsewhere();

        if (_stage == Stage.Paused)
        {
            Enter(Stage.Resuming, now);

            return new RunnerCommand.None();
        }

        if (now - _stageSince < ResumeSettleSeconds) return new RunnerCommand.None();

        _stage = Stage.Ready;

        return new RunnerCommand.Resumed(_resumesAtOtherBenches ? ResumedAnyBenchReason : ResumedReason);
    }

    private RunnerCommand WaitElsewhere()
    {
        _stage = Stage.Paused;

        if (_hasToldElsewhere) return new RunnerCommand.None();

        _hasToldElsewhere = true;

        return new RunnerCommand.Paused(ElsewhereReason);
    }

    private RunnerCommand FinishStep() => FinishStep(_batchDone > 0 ? Math.Min(_batchDone, _sentQuantity) : _sentQuantity);

    private RunnerCommand FinishStep(int crafts)
    {
        var finished = Current;

        if (finished.IsTarget) _intermediateBatches = 0;

        _current = null;
        _stage = Stage.Ready;

        return new RunnerCommand.StepDone(finished.Step, crafts, finished.IsTarget, finished.OrderId);
    }

    private NextStep.Craft Current => _current ?? throw new InvalidOperationException("no step in progress");

    private void Enter(Stage stage, double now)
    {
        _stage = stage;
        _stageSince = now;
    }
}
