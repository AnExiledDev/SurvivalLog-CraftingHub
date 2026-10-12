using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

public readonly record struct CraftStep(Recipe Recipe, int Crafts);

public abstract record CraftPlan
{
    /// <summary>Crafts in the order to run them; the target is always last.</summary>
    public sealed record Ready(IReadOnlyList<CraftStep> Steps) : CraftPlan
    {
        public bool NeedsIntermediates => Steps.Count > 1;
    }

    /// <summary>Item id to how many are still missing after every intermediate that could be made was planned.</summary>
    public sealed record Short(IReadOnlyDictionary<int, int> Missing) : CraftPlan;
}

/// <summary>A plan walked to the end: its steps, what was short, and the stock left once it ran.</summary>
public sealed record CraftWalk(IReadOnlyList<CraftStep> Steps, IReadOnlyDictionary<int, int> Missing, IReadOnlyDictionary<int, int> Left);

/// <summary>
/// Works out what to craft, and in what order, to make a recipe from the stock on hand. Materials already owned are
/// used first; anything short is crafted from its own recipe, down to a depth cap. Stock is reserved as the plan is
/// built, so two parts of one plan never count the same planks twice, and leftovers from a batch feed later steps.
/// </summary>
public static class CraftPlanner
{
    public static CraftPlan Plan(RecipeBook book, IReadOnlyDictionary<int, int> stock, Recipe target, int crafts, int maxDepth)
    {
        var walk = Walk(book, stock, target, crafts, maxDepth);

        return walk.Missing.Count == 0 ? new CraftPlan.Ready(walk.Steps) : new CraftPlan.Short(walk.Missing);
    }

    /// <summary>
    /// The plan worked through to the end even when something is short, with the stock it would leave behind, so a
    /// whole queue can be costed order by order. What is short is taken as found, so later orders do not count it twice.
    /// </summary>
    public static CraftWalk Walk(RecipeBook book, IReadOnlyDictionary<int, int> stock, Recipe target, int crafts, int maxDepth)
    {
        var run = new Run(book, stock, maxDepth);

        run.Craft(target, Math.Max(1, crafts), depth: 0);

        return new CraftWalk(run.Steps, run.Missing, run.Stock);
    }

    private sealed class Run
    {
        private readonly RecipeBook _book;
        private readonly Dictionary<int, int> _stock;
        private readonly int _maxDepth;

        public Run(RecipeBook book, IReadOnlyDictionary<int, int> stock, int maxDepth)
        {
            _book = book;
            _stock = stock.ToDictionary(pair => pair.Key, pair => pair.Value);
            _maxDepth = maxDepth;
        }

        public List<CraftStep> Steps { get; } = new();

        public Dictionary<int, int> Missing { get; } = new();

        public IReadOnlyDictionary<int, int> Stock => _stock;

        public void Craft(Recipe recipe, int crafts, int depth)
        {
            foreach (var (itemId, perCraft) in recipe.Materials) Need(itemId, perCraft * crafts, depth);

            AddStep(recipe, crafts);
            _stock[recipe.ProductId] = Owned(recipe.ProductId) + crafts * recipe.Yield;
        }

        private void Need(int itemId, int amount, int depth)
        {
            var taken = Math.Min(Owned(itemId), amount);
            var rest = amount - taken;
            _stock[itemId] = Owned(itemId) - taken;

            if (rest == 0) return;

            var producer = _book.ProducerOf(itemId);

            // The depth cap also ends a recipe that, however indirectly, needs its own product.
            if (producer == null || depth >= _maxDepth)
            {
                Missing[itemId] = Missing.GetValueOrDefault(itemId) + rest;

                return;
            }

            Craft(producer, crafts: (rest + producer.Yield - 1) / producer.Yield, depth + 1);
            _stock[itemId] = Owned(itemId) - rest;
        }

        private void AddStep(Recipe recipe, int crafts)
        {
            if (Steps.Count > 0 && Steps[^1].Recipe.Key == recipe.Key)
            {
                Steps[^1] = new CraftStep(recipe, Steps[^1].Crafts + crafts);

                return;
            }

            Steps.Add(new CraftStep(recipe, crafts));
        }

        private int Owned(int itemId) => _stock.GetValueOrDefault(itemId);
    }
}
