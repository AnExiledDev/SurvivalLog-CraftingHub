using CraftingHub.Core;
using Xunit;

namespace CraftingHub.Tests;

public class CraftWarmUpTests
{
    /// <summary>
    /// The warm-up only helps the first workbench open if it goes down the same paths: marks found through
    /// intermediates, an order that makes them first, a top-up, a drawn panel and a run that picks its first step.
    /// </summary>
    [Fact]
    public void The_warm_up_plans_costs_tops_up_and_draws_like_a_real_bench()
    {
        var result = CraftWarmUp.Run();

        Assert.Equal(1, result.Marks);
        Assert.Equal(OrderState.MakesFirst, result.LastOrder);
        Assert.Equal(1, result.TopUpOrders);
        Assert.True(result.HasView);
        Assert.Equal(new RunnerCommand.ClickRecipe("plank"), result.FirstRunStep);
    }

    [Fact]
    public void The_warm_up_can_run_again_and_works_the_same_out()
    {
        Assert.Equal(CraftWarmUp.Run(), CraftWarmUp.Run());
    }
}
