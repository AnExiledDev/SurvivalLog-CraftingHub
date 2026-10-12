using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>Keep <see cref="Count"/> of an item in the home. <see cref="RecipeKey"/> is the recipe the player set it on.</summary>
public readonly record struct KeepTarget(int ItemId, string RecipeKey, string Name, int Count)
{
    public const int MaxCount = 9999;
}

/// <summary>One save's keep-stocked targets, at most one per item: setting a target from another recipe for the same item replaces it.</summary>
public sealed class KeepTargets
{
    private readonly List<KeepTarget> _targets = new();

    public KeepTargets()
    {
    }

    public KeepTargets(IEnumerable<KeepTarget> targets)
    {
        foreach (var target in targets) Set(target);
    }

    public IReadOnlyList<KeepTarget> All => _targets;

    /// <summary>Adds or replaces the item's target. A count of zero or less removes it.</summary>
    public void Set(KeepTarget target)
    {
        var index = _targets.FindIndex(existing => existing.ItemId == target.ItemId);

        if (target.Count <= 0)
        {
            if (index >= 0) _targets.RemoveAt(index);

            return;
        }

        var capped = target with { Count = Math.Min(target.Count, KeepTarget.MaxCount) };

        if (index >= 0) _targets[index] = capped;
        else _targets.Add(capped);
    }

    public void Remove(int itemId) => _targets.RemoveAll(target => target.ItemId == itemId);

    /// <summary>Recipe key to target count, for every recipe in the book whose product has a target.</summary>
    public IReadOnlyDictionary<string, int> ByRecipe(RecipeBook book)
    {
        var counts = _targets.ToDictionary(target => target.ItemId, target => target.Count);

        return book.All.Where(recipe => counts.ContainsKey(recipe.ProductId)).ToDictionary(recipe => recipe.Key, recipe => counts[recipe.ProductId]);
    }
}
