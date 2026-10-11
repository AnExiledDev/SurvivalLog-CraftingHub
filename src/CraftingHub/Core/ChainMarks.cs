using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>
/// The recipes the game calls uncraftable that become craftable by making intermediates first, each with a line
/// saying what gets made on the way.
/// </summary>
public static class ChainMarks
{
    public static IReadOnlyDictionary<string, string> Find(RecipeBook book, IReadOnlyDictionary<int, int> stock, IReadOnlySet<string> craftableNow, int maxDepth)
    {
        var marks = new Dictionary<string, string>();

        if (maxDepth <= 0) return marks;

        foreach (var recipe in book.All)
        {
            if (recipe.IsLocked || craftableNow.Contains(recipe.Key)) continue;

            if (CraftPlanner.Plan(book, stock, recipe, 1, maxDepth) is CraftPlan.Ready { NeedsIntermediates: true } plan) marks[recipe.Key] = MakesFirst(plan.Steps);
        }

        return marks;
    }

    /// <summary>"Makes first: Plank ×2, Rope ×1" for every step but the target itself.</summary>
    public static string MakesFirst(IReadOnlyList<CraftStep> steps) =>
        "Makes first: " + string.Join(", ", steps.Take(steps.Count - 1).Select(step => $"{step.Recipe.Name} ×{step.Crafts}"));
}
