using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

/// <summary>The run loop against what the workbench really reports: stamina, a held hand, unreadable moments, late stock and real result counts.</summary>
public class RunLoopTests
{
    private const long RunBench = 1;

    private static readonly CraftStep PlankStep = new(Bench.PlankRecipe, 3);
    private static readonly IReadOnlyDictionary<int, int> Empty = new Dictionary<int, int>();
    private static readonly IReadOnlyDictionary<int, int> OneCraft = Bench.Mats((Bench.Log, 1));
    private static readonly BenchView Closed = new(false, false, false, 0, Empty, 0);

    private static BenchView Idle(IReadOnlyDictionary<int, int>? contents = null, int max = 0) => new(true, false, false, max, contents ?? Empty, 0, RunBench);

    private static NextStep PlankNext() => new NextStep.Craft(PlankStep, IsTarget: true);

    [Fact]
    public void Out_of_stamina_on_a_laid_out_bench_stops_at_once_saying_what_a_craft_needs_and_what_you_have()
    {
        var runner = new QueueRunner();
        var tired = Idle(OneCraft, 0) with { PreviewReady = true, MaxByStamina = true, Stamina = new StaminaRead(3, 8) };

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        Assert.IsType<RunnerCommand.None>(runner.Tick(tired, 0.1, PlankNext));

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(tired, 0.5, PlankNext));
        Assert.Equal("Out of stamina: Plank needs 8 per craft, you have 3", stopped.Reason);
    }

    [Fact]
    public void A_fractional_stamina_cost_is_told_rounded_up_so_having_what_is_shown_always_covers_a_craft()
    {
        var runner = new QueueRunner();
        var tired = Idle(OneCraft, 0) with { PreviewReady = true, MaxByStamina = true, Stamina = new StaminaRead(7.1f, 7.2f) };

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);
        runner.Tick(tired, 0.1, PlankNext);

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(tired, 0.5, PlankNext));
        Assert.Equal("Out of stamina: Plank needs 8 per craft, you have 7", stopped.Reason);
    }

    [Fact]
    public void Twice_a_crafts_materials_on_the_bench_is_clicked_again_and_never_blamed_on_stamina()
    {
        var runner = new QueueRunner();
        var doubled = Idle(Bench.Mats((Bench.Log, 2)), 0) with { PreviewReady = true, MaxByStamina = true, Stamina = new StaminaRead(3, 8) };

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(doubled, QueueRunner.RecipeClickRetrySeconds, PlankNext));

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(doubled, QueueRunner.FillTimeoutSeconds + 0.1, PlankNext));
        Assert.Equal("the workbench holds more than one craft's worth of materials for Plank; the game crafts only from exactly one craft's worth, so take the extra off the workbench", stopped.Reason);
    }

    [Fact]
    public void A_locked_recipe_on_a_laid_out_bench_stops_with_the_games_reason()
    {
        var runner = new QueueRunner();
        var locked = Idle(OneCraft, 0) with { PreviewReady = true, CraftLocked = true, LockText = "Needs production level 3" };

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        Assert.IsType<RunnerCommand.None>(runner.Tick(locked, 0.1, PlankNext));

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(locked, 0.5, PlankNext));
        Assert.Equal("Plank is locked: Needs production level 3", stopped.Reason);
    }

    [Fact]
    public void A_laid_out_bench_the_game_matched_to_no_craft_is_clicked_again()
    {
        var runner = new QueueRunner();
        var unmatched = Idle(OneCraft, 0) with { PreviewReady = true };

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(unmatched, QueueRunner.RecipeClickRetrySeconds, PlankNext));
    }

    [Fact]
    public void A_held_hand_delays_the_first_click_until_it_clears_and_does_not_stop_the_run()
    {
        var runner = new QueueRunner();
        var held = Idle() with { IsHandHeld = true };

        runner.Start();

        Assert.IsType<RunnerCommand.None>(runner.Tick(held, 0, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(held, QueueRunner.FillTimeoutSeconds + 1, PlankNext));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), QueueRunner.FillTimeoutSeconds + 2, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(OneCraft, 3), QueueRunner.FillTimeoutSeconds + 2.1, PlankNext));
        Assert.Equal(new RunnerCommand.Craft(3), runner.Tick(Idle(OneCraft, 3), QueueRunner.FillTimeoutSeconds + 2.5, PlankNext));
        Assert.True(runner.IsRunning);
    }

    [Fact]
    public void A_hand_held_after_the_click_holds_the_fill_clock_and_the_click_goes_again_once_it_clears()
    {
        var runner = new QueueRunner();
        var held = Idle() with { IsHandHeld = true };

        runner.Start();
        runner.Tick(Idle(), 0, PlankNext);

        for (var now = 0.5; now < QueueRunner.FillTimeoutSeconds + 2; now += 0.5) Assert.IsType<RunnerCommand.None>(runner.Tick(held, now, PlankNext));

        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), QueueRunner.FillTimeoutSeconds + 2, PlankNext));
    }

    [Fact]
    public void A_hand_that_stays_held_pauses_the_run_until_the_game_lets_go_of_it_with_the_workbench_still_open()
    {
        var runner = new QueueRunner();
        var held = Idle() with { IsHandHeld = true };
        var pausedAt = QueueRunner.HeldHandSeconds + 0.1;
        var releasedAt = pausedAt + 3 + QueueRunner.ResumeSettleSeconds;

        runner.Start();
        runner.Tick(held, 0, PlankNext);

        var paused = Assert.IsType<RunnerCommand.Paused>(runner.Tick(held, pausedAt, PlankNext));
        Assert.Contains("close it and open it again", paused.Reason);
        Assert.IsType<RunnerCommand.None>(runner.Tick(held, pausedAt + 1, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(held, pausedAt + 2 + QueueRunner.ResumeSettleSeconds, PlankNext));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), releasedAt, PlankNext));
        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(Idle(), releasedAt + QueueRunner.ResumeSettleSeconds, PlankNext));
    }

    [Fact]
    public void A_hand_that_stays_held_pauses_the_run_until_the_workbench_is_closed_and_opened_again()
    {
        var runner = new QueueRunner();
        var held = Idle() with { IsHandHeld = true };
        var reopenedAt = QueueRunner.HeldHandSeconds + 5;

        runner.Start();
        runner.Tick(held, 0, PlankNext);
        runner.Tick(held, QueueRunner.HeldHandSeconds + 0.1, PlankNext);
        runner.Tick(Closed, reopenedAt - 1, PlankNext);
        runner.Tick(held, reopenedAt, PlankNext);

        Assert.IsType<RunnerCommand.Resumed>(runner.Tick(held, reopenedAt + QueueRunner.ResumeSettleSeconds, PlankNext));
    }

    [Fact]
    public void With_resuming_off_a_hand_that_stays_held_stops_the_run_asking_for_the_workbench_to_be_reopened()
    {
        var runner = new QueueRunner(resumesOnReopen: false);
        var held = Idle() with { IsHandHeld = true };

        runner.Start();
        runner.Tick(held, 0, PlankNext);

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(held, QueueRunner.HeldHandSeconds + 0.1, PlankNext));
        Assert.Contains("close it and open it again", stopped.Reason);
    }

    [Fact]
    public void A_stale_batch_with_a_held_hand_pauses_for_a_reopen_instead_of_clicking()
    {
        var runner = new QueueRunner();
        var stuck = new BenchView(true, true, false, 0, Empty, 2, RunBench, BatchActive: true, IsHandHeld: true);

        runner.Start();

        Assert.IsType<RunnerCommand.None>(runner.Tick(stuck, 0, PlankNext));
        Assert.IsType<RunnerCommand.Notice>(runner.Tick(stuck, QueueRunner.StaleBatchSeconds, PlankNext));
        Assert.IsType<RunnerCommand.Paused>(runner.Tick(stuck, QueueRunner.StaleBatchSeconds + 0.1, PlankNext));
    }

    [Fact]
    public void Waiting_for_a_workbench_read_twice_then_crafting_does_not_stop_the_run()
    {
        var runner = new QueueRunner();
        var plans = new Queue<NextStep>(new[] { new NextStep.Wait("could not read the workbench"), new NextStep.Wait("could not read the workbench"), PlankNext() });

        runner.Start();

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 0, plans.Dequeue));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 0.1, plans.Dequeue));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle(), 0.2, plans.Dequeue));
        Assert.True(runner.IsRunning);
    }

    [Fact]
    public void A_workbench_that_stays_unreadable_stops_the_run_with_the_reason()
    {
        var runner = new QueueRunner();
        NextStep Unreadable() => new NextStep.Wait("could not read the workbench");

        runner.Start();

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 0, Unreadable));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), QueueRunner.WaitLimitSeconds - 0.1, Unreadable));

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), QueueRunner.WaitLimitSeconds + 0.1, Unreadable));
        Assert.Equal("could not read the workbench", stopped.Reason);
    }

    [Fact]
    public void A_shortage_right_after_a_step_is_planned_again_once_the_game_rebuilds_its_list()
    {
        var runner = new QueueRunner();
        var plans = new Queue<NextStep>(new[] { PlankNext(), new NextStep.Blocked("can't make Plank: short of Log x1"), PlankNext() });

        FinishOneStep(runner, plans.Dequeue);

        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 1.2, plans.Dequeue));
        Assert.IsType<RunnerCommand.None>(runner.Tick(Idle(), 1.5, plans.Dequeue));
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle() with { Refreshes = 1 }, 1.6, plans.Dequeue));
    }

    [Fact]
    public void A_shortage_still_there_when_planned_again_stops_the_run()
    {
        var runner = new QueueRunner();
        var plans = new Queue<NextStep>(new[] { PlankNext(), new NextStep.Blocked("short"), new NextStep.Blocked("short of Log x1") });

        FinishOneStep(runner, plans.Dequeue);
        runner.Tick(Idle(), 1.2, plans.Dequeue);

        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), 1.2 + QueueRunner.RefreshWaitSeconds, plans.Dequeue));
        Assert.Equal("short of Log x1", stopped.Reason);
    }

    [Fact]
    public void A_shortage_at_the_start_of_a_run_stops_it_at_once()
    {
        var runner = new QueueRunner();

        runner.Start();

        Assert.IsType<RunnerCommand.Stopped>(runner.Tick(Idle(), 0, () => new NextStep.Blocked("short of Log x1")));
    }

    [Fact]
    public void A_bench_that_makes_one_per_craft_still_makes_every_craft_queued()
    {
        var queue = new CraftQueue();
        var runner = new QueueRunner();
        var game = new OnePerCraftBench();
        var crafts = 0;
        var made = 0;
        NextStep Next() => queue.Head is { } head ? new NextStep.Craft(new CraftStep(Bench.PlankRecipe, head.Crafts), IsTarget: true, OrderId: head.Id) : new NextStep.Finished();

        queue.Add("plank", "Plank", 4);
        runner.Start();

        for (var now = 0.0; runner.IsRunning && now < 120; now += 0.1)
        {
            switch (runner.Tick(game.View, now, Next))
            {
                case RunnerCommand.ClickRecipe:
                    game.LayOut();
                    break;
                case RunnerCommand.Craft:
                    crafts++;
                    game.Craft();
                    break;
                case RunnerCommand.ConfirmResult:
                    game.Confirm();
                    break;
                case RunnerCommand.StepDone done:
                    made += done.Crafts;
                    queue.Progress(done.OrderId, done.Crafts);
                    break;
            }

            game.Advance();
        }

        Assert.Equal(4, crafts);
        Assert.Equal(4, made);
        Assert.Empty(queue.Entries);
    }

    [Fact]
    public void A_batch_counts_its_order_down_as_each_craft_finishes_and_the_order_leaves_with_its_last()
    {
        var queue = new CraftQueue();
        var runner = new QueueRunner();
        var game = new BatchBench();
        var seen = new List<int>();
        var credited = 0;
        NextStep Next() => queue.Head is { } head ? new NextStep.Craft(new CraftStep(Bench.PlankRecipe, head.Crafts), IsTarget: true, OrderId: head.Id) : new NextStep.Finished();

        queue.Add("plank", "Plank", 3);
        runner.Start();

        RunBatches(runner, game, Next, command =>
        {
            var crafts = command switch
            {
                RunnerCommand.Made made => made.Crafts,
                RunnerCommand.StepDone done => done.Uncredited,
                _ => 0,
            };

            queue.Progress(queue.Head?.Id ?? 0, crafts);
            credited += crafts;
            seen.Add(queue.Head?.Crafts ?? 0);
        });

        Assert.Equal(new[] { 3, 2, 1, 0 }, seen.Distinct());
        Assert.Equal(3, credited);
        Assert.Equal(1, game.Batches);
        Assert.Empty(queue.Entries);
    }

    [Fact]
    public void Crafts_a_batch_finished_before_the_run_was_stopped_stay_taken_off_the_order()
    {
        var queue = new CraftQueue();
        var runner = new QueueRunner();
        var game = new BatchBench();
        NextStep Next() => queue.Head is { } head ? new NextStep.Craft(new CraftStep(Bench.PlankRecipe, head.Crafts), IsTarget: true, OrderId: head.Id) : new NextStep.Finished();

        queue.Add("plank", "Plank", 3);
        runner.Start();

        RunBatches(runner, game, Next, command =>
        {
            if (command is RunnerCommand.Made made)
            {
                queue.Progress(made.OrderId, made.Crafts);
                runner.Stop("stopped by the player");
            }
        });

        Assert.Equal(2, Assert.Single(queue.Entries).Crafts);
    }

    [Fact]
    public void An_intermediate_batch_takes_nothing_off_any_order()
    {
        var runner = new QueueRunner();
        var game = new BatchBench();
        var commands = new List<RunnerCommand>();
        var steps = 0;
        NextStep Next() => steps++ == 0 ? new NextStep.Craft(new CraftStep(Bench.PlankRecipe, 3), IsTarget: false, OrderId: 7) : new NextStep.Finished();

        runner.Start();
        RunBatches(runner, game, Next, commands.Add);

        Assert.DoesNotContain(commands, command => command is RunnerCommand.Made);
        Assert.Equal(3, commands.OfType<RunnerCommand.StepDone>().Single().Uncredited);
    }

    /// <summary>Plays the game's side of a run against <paramref name="game"/>, handing every runner command to <paramref name="onCommand"/>.</summary>
    private static void RunBatches(QueueRunner runner, BatchBench game, Func<NextStep> next, Action<RunnerCommand> onCommand)
    {
        for (var now = 0.0; runner.IsRunning && now < 120; now += 0.1)
        {
            var command = runner.Tick(game.View, now, next);

            switch (command)
            {
                case RunnerCommand.ClickRecipe:
                    game.LayOut();
                    break;
                case RunnerCommand.Craft craft:
                    game.Craft(craft.Quantity);
                    break;
                case RunnerCommand.ConfirmResult:
                    game.Confirm();
                    break;
            }

            onCommand(command);
            game.Advance();
        }
    }

    [Fact]
    public void A_finished_craft_the_game_posted_no_result_for_credits_nothing_and_is_tried_again()
    {
        var runner = new QueueRunner();
        var counted = new ResultCount(5, 5);

        runner.Start();

        var notice = Assert.IsType<RunnerCommand.Notice>(SendAndFinish(runner, 0, counted, counted));
        Assert.Equal("the game made nothing from the Plank craft just sent; trying again", notice.Text);
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), runner.Tick(Idle() with { Results = counted }, 1.2, PlankNext));
    }

    [Fact]
    public void A_second_craft_in_a_row_that_makes_nothing_stops_the_run_instead_of_sending_it_forever()
    {
        var runner = new QueueRunner();
        var counted = new ResultCount(5, 5);

        runner.Start();
        SendAndFinish(runner, 0, counted, counted);

        var stopped = Assert.IsType<RunnerCommand.Stopped>(SendAndFinish(runner, 2, counted, counted));
        Assert.Equal("the game made nothing from Plank twice in a row; press Craft queue to try again", stopped.Reason);
    }

    [Fact]
    public void A_craft_that_makes_something_between_two_empty_ones_keeps_the_run_going()
    {
        var runner = new QueueRunner();
        var before = new ResultCount(5, 5);
        var after = new ResultCount(6, 6);

        runner.Start();
        SendAndFinish(runner, 0, before, before);

        Assert.Equal(1, Assert.IsType<RunnerCommand.StepDone>(SendAndFinish(runner, 2, before, after)).Crafts);
        Assert.IsType<RunnerCommand.Notice>(SendAndFinish(runner, 4, after, after));
    }

    [Fact]
    public void A_craft_finished_in_the_moment_before_the_workbench_closed_is_counted()
    {
        var runner = new QueueRunner();

        runner.Start();
        runner.Tick(Idle() with { Results = new ResultCount(0, 0) }, 0, PlankNext);
        runner.Tick(Idle(OneCraft, 3) with { Results = new ResultCount(0, 0) }, 0.1, PlankNext);
        runner.Tick(Idle(OneCraft, 3) with { Results = new ResultCount(0, 0) }, 0.4, PlankNext);
        runner.Tick(new BenchView(true, true, false, 0, Empty, 0, RunBench, BatchActive: true, IsCrafting: true, Results: new ResultCount(2, 2)), 1, PlankNext);

        var closed = Closed with { Results = new ResultCount(3, 3), ResultsProven = true };
        Assert.Equal(3, Assert.IsType<RunnerCommand.StepDone>(runner.Tick(closed, 1.1, PlankNext)).Crafts);
    }

    [Fact]
    public void While_the_result_counter_has_never_fired_a_step_with_nothing_counted_is_credited_as_sent_and_the_run_goes_on()
    {
        var runner = new QueueRunner();
        var unmoved = new ResultCount(0, 0);

        runner.Start();

        Assert.Equal(4, Assert.IsType<RunnerCommand.StepDone>(SendAndFinish(runner, 0, unmoved, unmoved, isProven: false, crafts: 4)).Crafts);
        Assert.Equal(4, Assert.IsType<RunnerCommand.StepDone>(SendAndFinish(runner, 2, unmoved, unmoved, isProven: false, crafts: 4)).Crafts);
        Assert.True(runner.IsRunning);
    }

    [Fact]
    public void A_step_during_which_the_result_counter_first_fires_is_credited_by_its_count()
    {
        var runner = new QueueRunner();

        runner.Start();

        var done = Assert.IsType<RunnerCommand.StepDone>(SendAndFinish(runner, 0, new ResultCount(0, 0), new ResultCount(1, 1), isProven: true, crafts: 4));
        Assert.Equal(1, done.Crafts);
    }

    /// <summary>
    /// A ready run sends a step of Plank, and the game finishes it with its result count moved from one to the other.
    /// The counter is unproven when the step is sent and as proven as told when it ends.
    /// </summary>
    private static RunnerCommand SendAndFinish(QueueRunner runner, double at, ResultCount before, ResultCount after, bool isProven = true, int crafts = 3)
    {
        NextStep Next() => new NextStep.Craft(new CraftStep(Bench.PlankRecipe, crafts), IsTarget: true);

        runner.Tick(Idle() with { Results = before }, at, Next);
        runner.Tick(Idle(OneCraft, crafts) with { Results = before }, at + 0.1, Next);
        Assert.Equal(new RunnerCommand.Craft(crafts), runner.Tick(Idle(OneCraft, crafts) with { Results = before }, at + 0.4, Next));
        Assert.IsType<RunnerCommand.ConfirmResult>(runner.Tick(new BenchView(true, false, true, 0, Empty, 0, RunBench, Results: after, ResultsProven: isProven), at + 1, Next));

        return runner.Tick(Idle() with { Results = after, ResultsProven = isProven }, at + 1.1, Next);
    }

    private static void FinishOneStep(QueueRunner runner, Func<NextStep> next)
    {
        runner.Start();
        runner.Tick(Idle(), 0, next);
        runner.Tick(Idle(OneCraft, 3), 0.1, next);
        Assert.IsType<RunnerCommand.Craft>(runner.Tick(Idle(OneCraft, 3), 0.4, next));
        Assert.IsType<RunnerCommand.ConfirmResult>(runner.Tick(new BenchView(true, false, true, 0, Empty, 3), 1, next));
        Assert.IsType<RunnerCommand.StepDone>(runner.Tick(Idle(), 1.1, next));
    }

    /// <summary>
    /// A workbench whose materials allow several crafts but that makes one per craft order, as the game does when it
    /// cannot refill the bench between the crafts of a batch.
    /// </summary>
    private sealed class OnePerCraftBench
    {
        private bool _isLaidOut;
        private bool _isCrafting;
        private bool _hasResult;
        private int _results;

        public BenchView View => new(
            IsOpen: true,
            IsBusy: _isCrafting,
            HasResult: _hasResult,
            MaxCrafts: _isLaidOut ? 4 : 0,
            Contents: _isLaidOut ? OneCraft : Empty,
            BatchDone: 0,
            BenchId: RunBench,
            IsCrafting: _isCrafting,
            PreviewReady: _isLaidOut,
            Results: new ResultCount(_results, _results),
            ResultsProven: _results > 0);

        public void LayOut() => _isLaidOut = true;

        public void Craft()
        {
            _isLaidOut = false;
            _isCrafting = true;
        }

        public void Confirm() => _hasResult = false;

        /// <summary>A frame passes: a craft in progress finishes and shows its result.</summary>
        public void Advance()
        {
            if (!_isCrafting) return;

            _isCrafting = false;
            _hasResult = true;
            _results++;
        }
    }

    /// <summary>
    /// A workbench that makes a whole batch from one craft order, one craft a frame, counting each in its batch and
    /// result counts, and shows the result screen after the last.
    /// </summary>
    private sealed class BatchBench
    {
        private bool _isLaidOut;
        private int _left;
        private bool _isBatch;
        private bool _hasResult;
        private int _batchDone;
        private int _results;

        public int Batches { get; private set; }

        public BenchView View => new(
            IsOpen: true,
            IsBusy: _left > 0,
            HasResult: _hasResult,
            MaxCrafts: _isLaidOut ? 3 : 0,
            Contents: _isLaidOut ? OneCraft : Empty,
            BatchDone: _batchDone,
            BenchId: RunBench,
            BatchActive: _isBatch,
            BatchSuccess: _batchDone,
            IsCrafting: _left > 0,
            PreviewReady: _isLaidOut,
            Results: new ResultCount(_results, _results),
            ResultsProven: _results > 0);

        public void LayOut() => _isLaidOut = true;

        public void Craft(int quantity)
        {
            _isLaidOut = false;
            _left = quantity;
            _isBatch = quantity > 1;
            _batchDone = 0;
            Batches++;
        }

        public void Confirm() => _hasResult = false;

        /// <summary>A frame passes: one craft of the batch finishes, and the last one shows the result.</summary>
        public void Advance()
        {
            if (_left == 0) return;

            _left--;
            _batchDone++;
            _results++;

            if (_left > 0) return;

            _isBatch = false;
            _hasResult = true;
        }
    }
}

public class StaminaViewTests
{
    private static readonly string[] Keys = { "plank", "rope" };

    [Fact]
    public void Every_listed_recipe_shows_what_a_craft_costs()
    {
        var shown = StaminaView.From(new StaminaRead(20.6f, 8), Keys, hasQueue: false);

        Assert.Equal(20, shown?.Have);
        Assert.Equal(8, shown?.Cost["plank"]);
        Assert.Equal(8, shown?.Cost["rope"]);
    }

    [Fact]
    public void Stamina_is_out_only_while_a_queued_craft_cannot_be_afforded()
    {
        var low = new StaminaRead(3, 8);
        var plenty = new StaminaRead(30, 8);

        Assert.True(StaminaView.From(low, Keys, hasQueue: true)?.IsOut);
        Assert.False(StaminaView.From(low, Keys, hasQueue: false)?.IsOut);
        Assert.False(StaminaView.From(plenty, Keys, hasQueue: true)?.IsOut);
    }

    [Fact]
    public void The_out_of_stamina_warning_clears_once_the_player_has_stamina_again_after_a_run_stopped_for_it()
    {
        var runner = new QueueRunner();
        var open = new BenchView(true, false, false, 0, new Dictionary<int, int>(), 0, 1);
        var tired = open with { Contents = Bench.Mats((Bench.Log, 1)), PreviewReady = true, MaxByStamina = true, Stamina = new StaminaRead(3, 8) };
        NextStep Plank() => new NextStep.Craft(new CraftStep(Bench.PlankRecipe, 3), IsTarget: true);

        runner.Start();
        runner.Tick(open, 0, Plank);
        runner.Tick(tired, 0.1, Plank);
        Assert.IsType<RunnerCommand.Stopped>(runner.Tick(tired, 0.5, Plank));

        var rested = new StaminaRead(100, 8);

        Assert.False(StaminaView.From(rested, Keys, hasQueue: true)?.IsOut);
        Assert.False(StaminaView.From(rested, Keys, hasQueue: false)?.IsOut);
    }

    [Fact]
    public void A_run_stopped_for_stamina_says_so_and_stays_out_while_the_game_reports_no_cost()
    {
        var runner = new QueueRunner();
        var open = new BenchView(true, false, false, 0, new Dictionary<int, int>(), 0, 1);
        var tired = open with { Contents = Bench.Mats((Bench.Log, 1)), PreviewReady = true, MaxByStamina = true, Stamina = new StaminaRead(3, 0) };
        NextStep Plank() => new NextStep.Craft(new CraftStep(Bench.PlankRecipe, 3), IsTarget: true);

        runner.Start();
        runner.Tick(open, 0, Plank);
        runner.Tick(tired, 0.1, Plank);
        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(tired, 0.5, Plank));

        Assert.True(stopped.IsOutOfStamina);
        Assert.True(StaminaView.From(new StaminaRead(3, 0), Keys, hasQueue: true, hasStoppedForStamina: true)?.IsOut);
        Assert.False(StaminaView.From(new StaminaRead(3, 0), Keys, hasQueue: true, hasStoppedForStamina: false)?.IsOut);
    }

    [Fact]
    public void After_a_stamina_stop_the_warning_clears_once_a_craft_is_seen_to_be_covered()
    {
        Assert.False(StaminaView.From(new StaminaRead(100, 8), Keys, hasQueue: true, hasStoppedForStamina: true)?.IsOut);
        Assert.True(StaminaView.From(new StaminaRead(5, 8), Keys, hasQueue: true, hasStoppedForStamina: true)?.IsOut);
    }

    [Fact]
    public void A_run_that_stops_for_any_other_reason_is_not_called_out_of_stamina()
    {
        var runner = new QueueRunner();
        var open = new BenchView(true, false, false, 0, new Dictionary<int, int>(), 0, 1);
        var locked = open with { Contents = Bench.Mats((Bench.Log, 1)), PreviewReady = true, CraftLocked = true, Stamina = new StaminaRead(3, 0) };
        NextStep Plank() => new NextStep.Craft(new CraftStep(Bench.PlankRecipe, 3), IsTarget: true);

        runner.Start();
        runner.Tick(open, 0, Plank);
        runner.Tick(locked, 0.1, Plank);
        var stopped = Assert.IsType<RunnerCommand.Stopped>(runner.Tick(locked, 0.5, Plank));

        Assert.False(stopped.IsOutOfStamina);
    }

    [Fact]
    public void Crafts_that_cost_no_stamina_are_never_out_and_list_no_cost()
    {
        var shown = StaminaView.From(new StaminaRead(10, 0), Keys, hasQueue: true);

        Assert.False(shown?.IsOut);
        Assert.Empty(shown?.Cost ?? new Dictionary<string, int> { ["x"] = 1 });
    }

    [Fact]
    public void A_fractional_cost_is_shown_rounded_up_so_having_it_always_covers_a_craft()
    {
        var shown = StaminaView.From(new StaminaRead(7.1f, 7.2f), Keys, hasQueue: true);

        Assert.Equal(8, shown?.Cost["plank"]);
        Assert.Equal(7, shown?.Have);
        Assert.True(shown?.IsOut);
    }

    [Fact]
    public void Unreadable_stamina_shows_as_nothing()
    {
        Assert.Null(StaminaView.From(null, Keys, hasQueue: true));
    }

    [Fact]
    public void Stamina_the_game_left_unanswered_shows_as_nothing_rather_than_as_empty_and_free()
    {
        Assert.Null(StaminaView.From(new StaminaRead(0, 0), Keys, hasQueue: true));
    }

    [Fact]
    public void The_page_gets_stamina_as_have_out_and_the_cost_per_recipe_or_null()
    {
        var keep = new KeepView(false, Array.Empty<KeepTarget>(), new Dictionary<string, int>());
        var stamina = new StaminaView(3, true, new Dictionary<string, int> { ["plank"] = 8, ["rope"] = 0 });

        var json = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", keep, stamina: stamina);
        var unread = HubView.Build(new Dictionary<string, string>(), Array.Empty<QueueEntry>(), false, "", keep);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        using var unreadParsed = System.Text.Json.JsonDocument.Parse(unread);
        var shown = parsed.RootElement.GetProperty("stamina");
        Assert.Equal(3, shown.GetProperty("have").GetInt32());
        Assert.True(shown.GetProperty("out").GetBoolean());
        Assert.Equal(8, shown.GetProperty("cost").GetProperty("plank").GetInt32());
        Assert.False(shown.GetProperty("cost").TryGetProperty("rope", out _));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unreadParsed.RootElement.GetProperty("stamina").ValueKind);
    }
}
