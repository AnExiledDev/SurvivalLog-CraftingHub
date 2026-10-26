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

    /// <summary>Moves on every change to the targets, so a reader can tell they are unchanged without comparing them.</summary>
    public int Version { get; private set; }

    /// <summary>Adds or replaces the item's target. A count of zero or less removes it.</summary>
    public void Set(KeepTarget target)
    {
        var index = _targets.FindIndex(existing => existing.ItemId == target.ItemId);

        if (target.Count <= 0)
        {
            if (index < 0) return;

            _targets.RemoveAt(index);
            Version++;

            return;
        }

        var capped = target with { Count = Math.Min(target.Count, KeepTarget.MaxCount) };

        if (index >= 0) _targets[index] = capped;
        else _targets.Add(capped);

        Version++;
    }

    public void Remove(int itemId)
    {
        if (_targets.RemoveAll(target => target.ItemId == itemId) > 0) Version++;
    }

    /// <summary>Recipe key to target count, for every recipe in the book whose product has a target.</summary>
    public IReadOnlyDictionary<string, int> ByRecipe(RecipeBook book)
    {
        var counts = _targets.ToDictionary(target => target.ItemId, target => target.Count);

        return book.All.Where(recipe => counts.ContainsKey(recipe.ProductId)).ToDictionary(recipe => recipe.Key, recipe => counts[recipe.ProductId]);
    }
}

/// <summary>
/// What keep-stocked crafts may not use. A kept item counts only above its target, so topping up one item never eats
/// another item that is being kept; <see cref="CraftPlanner"/> takes this as its floor.
/// </summary>
public static class KeptStock
{
    public static IReadOnlyDictionary<int, int> Floor(IEnumerable<KeepTarget> kept)
    {
        var floor = new Dictionary<int, int>();

        foreach (var target in kept) floor[target.ItemId] = Math.Max(0, target.Count);

        return floor;
    }
}
