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
/// <param name="IsHandHeld">
/// The game still holds the last craft's product in hand (HandMadeState 2 or 3). It ignores recipe clicks and crafts
/// until that clears, which it does when the bag next refreshes, or when the workbench is opened again.
/// </param>
/// <param name="PreviewReady">The game has recounted what is laid out and matched it to a recipe.</param>
/// <param name="MaxByStamina">Stamina, not materials, sets <see cref="MaxCrafts"/>.</param>
/// <param name="CraftLocked">The laid-out recipe is locked; <see cref="LockText"/> is the game's reason.</param>
/// <param name="Stamina">The player's stamina and a craft's cost; null when it could not be read.</param>
/// <param name="Results">
/// Crafts the game has finished at this workbench since the plugin loaded, window open or not; null when they are not
/// being counted.
/// </param>
/// <param name="Refreshes">Goes up every time the game rebuilds the recipe list, which it does after items move.</param>
/// <param name="ResultsProven">
/// The game has been seen handing over a finished craft since the plugin loaded, so <see cref="Results"/> really moves.
/// Until then a count that stays put may only mean nothing is counting.
/// </param>
public readonly record struct BenchView(
    bool IsOpen,
    bool IsBusy,
    bool HasResult,
    int MaxCrafts,
    IReadOnlyDictionary<int, int> Contents,
    int BatchDone,
    long BenchId = 0,
    bool BatchActive = false,
    int BatchSuccess = 0,
    bool IsCrafting = false,
    bool IsHandHeld = false,
    bool PreviewReady = false,
    bool MaxByStamina = false,
    bool CraftLocked = false,
    string LockText = "",
    StaminaRead? Stamina = null,
    ResultCount? Results = null,
    int Refreshes = 0,
    bool ResultsProven = false);

/// <summary>The player's stamina and what one craft costs, as the game works them out for the workbench.</summary>
public readonly record struct StaminaRead(float Current, float PerCraft)
{
    public int Have => (int)Math.Floor(Current);

    /// <summary>Rounded up, so a player told they have at least the cost can always afford the craft.</summary>
    public int Cost => (int)Math.Ceiling(PerCraft);

    public bool IsShort => PerCraft > 0 && Current < PerCraft;

    /// <summary>Both zero is what the game's query leaves when nothing answered it, not a real reading.</summary>
    public bool IsUnanswered => Current == 0 && PerCraft == 0;
}

/// <summary>Crafts the game has finished, every one and the successful ones; both only go up.</summary>
public readonly record struct ResultCount(int All, int Successes);

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

    /// <summary>Nothing can be planned this moment (the workbench could not be read); ask again shortly.</summary>
    public sealed record Wait(string Reason) : NextStep;
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
    public const double FillTimeoutSeconds = 6;

    /// <summary>How long a bench may stay without the recipe's materials before the recipe is clicked again.</summary>
    public const double RecipeClickRetrySeconds = 0.75;

    public const double StartTimeoutSeconds = 4;
    public const double QuietDoneSeconds = 1.5;
    public const double CollectTimeoutSeconds = 4;
    public const int MaxIntermediateBatches = 30;
    public const double ResumeSettleSeconds = 1;

    /// <summary>A batch flag with nothing crafting for this long is taken as stale rather than a batch between crafts.</summary>
    public const double StaleBatchSeconds = 3;

    /// <summary>A workbench that stays busy and unchanged this long gets a status line, so a stall is not silent.</summary>
    public const double BusyNoticeSeconds = 30;

    /// <summary>A hand held this long, with nothing crafting, is taken as stuck until the workbench is opened again.</summary>
    public const double HeldHandSeconds = 10;

    /// <summary>How long the plan may say "wait" in a row before the run stops with its reason.</summary>
    public const double WaitLimitSeconds = 3;

    /// <summary>A shortage this soon after a step may come from a stock count taken before the game moved the product.</summary>
    public const double RecheckWindowSeconds = 1;

    /// <summary>How long a recheck waits for the game to rebuild its recipe list before planning again anyway.</summary>
    public const double RefreshWaitSeconds = 2;

    /// <summary>Steps in a row the game may finish without making anything before the run stops.</summary>
    public const int MaxEmptySteps = 2;

    private const string ClosedReason = "the workbench closed; the queue is kept, press Craft queue to carry on";
    private const string PausedReason = "the workbench closed; the queue carries on when you open it again";
    private const string PausedAnyBenchReason = "the workbench closed; the queue carries on when you open any workbench";
    private const string ResumedReason = "the workbench is open again; carrying on with the queue";
    private const string ResumedAnyBenchReason = "a workbench is open; carrying on with the queue here";
    private const string ElsewhereReason = "the queue carries on at the workbench it was running on; press Stop, then Craft queue, to run it here";
    private const string StaleBatchNotice = "the workbench shows a batch in progress with nothing being made; carrying on with the next craft";
    private const string BusyNotice = "waiting for the workbench: it has shown a craft in progress for a while with no change";
    private const string StuckPausedReason = "the workbench is stuck after the last craft; close it and open it again to carry on with the queue";
    private const string StuckStoppedReason = "the workbench is stuck after the last craft; close it and open it again, then press Craft queue";

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
    private double _clickedAt;
    private double? _quietSince;
    private int _intermediateBatches;
    private long _benchId;
    private bool _hasToldElsewhere;
    private double? _heldSince;
    private bool _needsReopen;
    private double? _waitingSince;
    private double _stepDoneAt = double.NegativeInfinity;
    private bool _hasRechecked;
    private double? _recheckSince;
    private int _recheckRefreshes;
    private ResultCount? _resultsAtSend;
    private int _made;
    private int _emptySteps;

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
        _heldSince = null;
        _needsReopen = false;
        _waitingSince = null;
        _stepDoneAt = double.NegativeInfinity;
        _hasRechecked = false;
        _recheckSince = null;
        _emptySteps = 0;
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

        if (!bench.IsOpen) return Interrupted(bench, now);

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

    /// <summary>
    /// The bench holds exactly one craft's materials, unit for unit. The game matches what is laid out to a recipe by
    /// exact units, so a bench with extra on it is matched to no craft.
    /// </summary>
    public static bool HoldsOneCraft(IReadOnlyDictionary<int, int> contents, IReadOnlyDictionary<int, int> materials) =>
        HoldsCraft(contents, materials, (count, perCraft) => count == perCraft);

    /// <summary>The bench holds the recipe's kinds of material and no other, with at least one craft's worth of each.</summary>
    public static bool HoldsAtLeastOneCraft(IReadOnlyDictionary<int, int> contents, IReadOnlyDictionary<int, int> materials) =>
        HoldsCraft(contents, materials, (count, perCraft) => count >= perCraft);

    private static bool HoldsCraft(IReadOnlyDictionary<int, int> contents, IReadOnlyDictionary<int, int> materials, Func<int, int, bool> isEnough)
    {
        if (contents.Count != materials.Count) return false;

        foreach (var (itemId, perCraft) in materials)
        {
            if (!contents.TryGetValue(itemId, out var count) || !isEnough(count, perCraft)) return false;
        }

        return true;
    }

    private RunnerCommand PickNext(BenchView bench, double now, Func<NextStep> next)
    {
        if (IsBusy(bench) || bench.HasResult) return WaitWhileBusy(bench, now);

        _busyWith = null;

        // The game drops recipe clicks while it still holds the last craft in hand, so the next step waits for it.
        if (bench.IsHandHeld) return WaitForHand(now);

        _heldSince = null;

        if (IsAwaitingRefresh(bench, now)) return new RunnerCommand.None();

        var step = next();

        if (step is not NextStep.Wait) _waitingSince = null;

        switch (step)
        {
            case NextStep.Finished:
                return Stop("queue finished", isDone: true);
            case NextStep.Wait wait:
                return WaitForPlan(wait.Reason, now);
            case NextStep.Blocked blocked:
                return IsTooSoonToTrust(now) ? Recheck(bench, now) : Stop(blocked.Reason);
            case NextStep.Craft craft:
                // Intermediates that never lead to a finished target mean the plan and the game disagree; better to
                // stop than to turn every log in the house into planks.
                if (!craft.IsTarget && ++_intermediateBatches > MaxIntermediateBatches) return Stop($"stopped after {MaxIntermediateBatches} intermediate crafts without finishing the target");

                _current = craft;
                _settledSince = null;
                _clickedAt = now;
                Enter(Stage.Filling, now);

                return new RunnerCommand.ClickRecipe(craft.Step.Recipe.Key);
            default:
                return Stop("nothing to craft");
        }
    }

    /// <summary>
    /// A hand held with nothing crafting clears when the game next refreshes the bag. One that stays held (at once,
    /// when the batch flag beside it is stale) is stuck, so the run asks for the workbench to be opened again, and
    /// carries on by itself if the game lets go of the hand first.
    /// </summary>
    private RunnerCommand WaitForHand(double now)
    {
        _heldSince ??= now;

        var limit = _isBatchStale ? 0 : HeldHandSeconds;

        return now - _heldSince >= limit ? StuckHand() : new RunnerCommand.None();
    }

    private RunnerCommand StuckHand()
    {
        _heldSince = null;
        _current = null;

        if (!_resumesOnReopen) return Stop(StuckStoppedReason);

        _stage = Stage.Paused;
        _needsReopen = true;
        _hasToldElsewhere = false;

        return new RunnerCommand.Paused(StuckPausedReason);
    }

    /// <summary>A moment the plan could not be worked out is waited out; one that lasts stops the run with its reason.</summary>
    private RunnerCommand WaitForPlan(string reason, double now)
    {
        _waitingSince ??= now;

        return now - _waitingSince >= WaitLimitSeconds ? Stop(reason) : new RunnerCommand.None();
    }

    /// <summary>
    /// A shortage found right after a step may come from stock counted before the game put the product away, so it is
    /// planned again once, after the game rebuilds its recipe list.
    /// </summary>
    private bool IsTooSoonToTrust(double now) => !_hasRechecked && now - _stepDoneAt < RecheckWindowSeconds;

    private RunnerCommand Recheck(BenchView bench, double now)
    {
        _hasRechecked = true;
        _recheckSince = now;
        _recheckRefreshes = bench.Refreshes;

        return new RunnerCommand.None();
    }

    /// <summary>A recheck holds the plan until the game rebuilds its recipe list, or a short while has passed.</summary>
    private bool IsAwaitingRefresh(BenchView bench, double now)
    {
        if (_recheckSince is not { } since) return false;

        if (bench.Refreshes == _recheckRefreshes && now - since < RefreshWaitSeconds) return true;

        _recheckSince = null;

        return false;
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

        if (bench.IsHandHeld) return HoldFillClock(now);

        _heldSince = null;

        var holdsOneCraft = HoldsOneCraft(bench.Contents, step.Recipe.Materials);

        if (holdsOneCraft && !IsBusy(bench) && bench.MaxCrafts < 1 && bench.PreviewReady) return HeldBackByGame(bench, now, step.Recipe);

        if (IsBusy(bench) || bench.MaxCrafts < 1 || !holdsOneCraft)
        {
            _settledSince = null;

            if (now - _stageSince > FillTimeoutSeconds) return Stop(FillFailure(bench, step.Recipe));

            return IsBusy(bench) || holdsOneCraft ? new RunnerCommand.None() : ClickAgain(step.Recipe.Key, now);
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
        _resultsAtSend = bench.Results;
        _made = 0;
        Enter(Stage.Crafting, now);

        return new RunnerCommand.Craft(_sentQuantity);
    }

    /// <summary>
    /// The game ignores clicks while it holds the last craft in hand, so the fill clock starts once the hand clears,
    /// and the click goes again then.
    /// </summary>
    private RunnerCommand HoldFillClock(double now)
    {
        _stageSince = now;
        _clickedAt = now - RecipeClickRetrySeconds;
        _settledSince = null;

        return WaitForHand(now);
    }

    /// <summary>
    /// The game matched the layout to the recipe and still allows no craft. Its own flags say why: the recipe is
    /// locked, or the player is out of stamina; read once the count has settled. With neither, the click goes again.
    /// </summary>
    private RunnerCommand HeldBackByGame(BenchView bench, double now, Recipe recipe)
    {
        if (!bench.CraftLocked && !bench.MaxByStamina)
        {
            _settledSince = null;

            return now - _stageSince > FillTimeoutSeconds ? Stop(FillFailure(bench, recipe)) : ClickAgain(recipe.Key, now);
        }

        _settledSince ??= now;

        if (now - _settledSince < SettleSeconds) return new RunnerCommand.None();

        if (bench.CraftLocked) return Stop(bench.LockText.Length > 0 ? $"{recipe.Name} is locked: {bench.LockText}" : $"{recipe.Name} is locked");

        return Stop(OutOfStamina(bench.Stamina, recipe.Name));
    }

    private static string OutOfStamina(StaminaRead? stamina, string recipeName) =>
        stamina is { Cost: > 0 } read
            ? $"Out of stamina: {recipeName} needs {read.Cost} per craft, you have {read.Have}"
            : $"Out of stamina: there is not enough stamina for a craft of {recipeName}";

    private string FillFailure(BenchView bench, Recipe recipe)
    {
        if (IsBusy(bench)) return $"the game did not lay out the materials for {recipe.Name}";

        if (HoldsOneCraft(bench.Contents, recipe.Materials)) return $"the materials for {recipe.Name} are laid out but the game allows no craft of it";

        return HoldsAtLeastOneCraft(bench.Contents, recipe.Materials)
            ? $"the workbench holds more than one craft's worth of materials for {recipe.Name}; the game crafts only from exactly one craft's worth, so take the extra off the workbench"
            : $"the game did not lay out the materials for {recipe.Name}";
    }

    /// <summary>
    /// The game ignores a recipe click while it still counts the last craft as in hand, which it clears only when it
    /// next refreshes the bag, so a click right after a craft is often lost. A bench without the recipe's materials
    /// gets the click again. Another click cannot undo one that landed: the game fills from what is already on the
    /// bench first and moves off only what the recipe does not use.
    /// </summary>
    private RunnerCommand ClickAgain(string recipeKey, double now)
    {
        if (now - _clickedAt < RecipeClickRetrySeconds) return new RunnerCommand.None();

        _clickedAt = now;

        return new RunnerCommand.ClickRecipe(recipeKey);
    }

    private RunnerCommand WaitForResult(BenchView bench, double now)
    {
        var isActive = IsBusy(bench) || bench.HasResult;

        CountBatch(bench);
        CountResults(bench);

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

        return now - _quietSince >= QuietDoneSeconds ? FinishStep(bench, now) : new RunnerCommand.None();
    }

    private RunnerCommand WaitForCollect(BenchView bench, double now)
    {
        CountBatch(bench);
        CountResults(bench);

        if (!bench.HasResult) return FinishStep(bench, now);

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
    /// The game's own count of finished crafts, taken from the moment the step was sent. Unlike the batch count it
    /// also sees crafts the game makes one at a time, so a step is credited with what was really made.
    /// </summary>
    private void CountResults(BenchView bench)
    {
        if (_resultsAtSend is not { } atSend || bench.Results is not { } seen) return;

        _made = Math.Max(_made, seen.All - atSend.All);
    }

    /// <summary>
    /// Read from the window just closed. Closing cancels the craft in progress without posting a result, so every
    /// result counted is a craft the game finished. Without that count, only batch successes since the last look add
    /// to the batch count.
    /// </summary>
    private void CountLateCrafts(BenchView closed)
    {
        CountResults(closed);

        if (!_hasSeenBatch) return;

        _batchDone += Math.Max(0, closed.BatchSuccess - _batchSuccess);
        _batchSuccess = Math.Max(_batchSuccess, closed.BatchSuccess);
    }

    /// <summary>
    /// Crafts this step is known to have made: the game's result count where it is trusted, the batch count otherwise,
    /// never more than were sent.
    /// </summary>
    private int Counted(BenchView bench) => Math.Min(IsCountTrusted(bench) ? Math.Max(_made, _batchDone) : _batchDone, _sentQuantity);

    /// <summary>
    /// Kept since the step was sent, and seen working by now. Read as the step ends, so the step during which the
    /// game first hands over a craft is already credited by its count.
    /// </summary>
    private bool IsCountTrusted(BenchView bench) => _resultsAtSend != null && bench.ResultsProven;

    /// <summary>
    /// The workbench closed under the run. Closing ends the craft in progress before it uses anything, so only crafts
    /// the game already finished count: a result on screen or a quiet finish ends the step as it would have with the
    /// bench open, otherwise the crafts counted so far, plus any finished in the moment before the close.
    /// Those are reported first; the next tick, still closed, pauses or stops the run.
    /// </summary>
    private RunnerCommand Interrupted(BenchView bench, double now)
    {
        if (_stage is Stage.Crafting or Stage.Collecting) CountLateCrafts(bench);

        var madeCrafts = Counted(bench);

        if (_stage == Stage.Collecting || (_stage == Stage.Crafting && _quietSince != null)) return FinishStep(bench, now);

        if (_stage == Stage.Crafting && madeCrafts > 0) return FinishStep(madeCrafts, now);

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
            _needsReopen = false;

            return new RunnerCommand.None();
        }

        // Paused on a stuck hand: opening the workbench again clears it, or the game letting go of the hand by itself.
        if (_needsReopen && bench.IsHandHeld) return new RunnerCommand.None();

        _needsReopen = false;

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

    /// <summary>
    /// A step that finished whole. Without a trusted result count, nothing counted by the batch means the step went
    /// as sent. With one, nothing counted means nothing was made, since the game posts a result for every craft.
    /// </summary>
    private RunnerCommand FinishStep(BenchView bench, double now)
    {
        var counted = Counted(bench);

        if (counted > 0) return FinishStep(counted, now);

        return IsCountTrusted(bench) ? NothingMade(now) : FinishStep(_sentQuantity, now);
    }

    private RunnerCommand FinishStep(int crafts, double now)
    {
        var finished = Current;

        if (finished.IsTarget) _intermediateBatches = 0;

        EndStep(now);
        _emptySteps = 0;

        return new RunnerCommand.StepDone(finished.Step, crafts, finished.IsTarget, finished.OrderId);
    }

    /// <summary>
    /// Nothing is credited, so the step is planned again from the stock. A second empty step in a row means the game
    /// keeps refusing it, and the run stops rather than send it forever.
    /// </summary>
    private RunnerCommand NothingMade(double now)
    {
        var name = Current.Step.Recipe.Name;

        EndStep(now);

        return ++_emptySteps >= MaxEmptySteps
            ? Stop($"the game made nothing from {name} twice in a row; press Craft queue to try again")
            : new RunnerCommand.Notice($"the game made nothing from the {name} craft just sent; trying again");
    }

    private void EndStep(double now)
    {
        _current = null;
        _stage = Stage.Ready;
        _stepDoneAt = now;
        _hasRechecked = false;
    }

    private NextStep.Craft Current => _current ?? throw new InvalidOperationException("no step in progress");

    private void Enter(Stage stage, double now)
    {
        _stage = stage;
        _stageSince = now;
        _heldSince = null;
    }
}
