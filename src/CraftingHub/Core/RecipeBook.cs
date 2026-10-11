using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>One recipe the open workbench lists, with what a single craft uses and makes.</summary>
public sealed record Recipe(string Key, string Name, int ProductId, int Yield, IReadOnlyDictionary<int, int> Materials, bool IsLocked);

/// <summary>
/// The recipes of the open workbench, and which of them makes a given item. Only recipes this bench lists are
/// known, so an intermediate that needs another station is never planned; it shows up as a missing material.
/// </summary>
public sealed class RecipeBook
{
    private readonly Dictionary<string, Recipe> _byKey = new();
    private readonly Dictionary<int, Recipe> _producerOf = new();

    public RecipeBook(IEnumerable<Recipe> recipes)
    {
        foreach (var recipe in recipes)
        {
            if (!_byKey.TryAdd(recipe.Key, recipe)) continue;

            // First listed wins, so the choice is stable between refreshes.
            if (!recipe.IsLocked && recipe.Yield > 0) _producerOf.TryAdd(recipe.ProductId, recipe);
        }
    }

    public IEnumerable<Recipe> All => _byKey.Values;

    public IEnumerable<int> MaterialIds => _byKey.Values.SelectMany(recipe => recipe.Materials.Keys).Distinct();

    public Recipe? Find(string key) => _byKey.TryGetValue(key, out var recipe) ? recipe : null;

    public Recipe? ProducerOf(int itemId) => _producerOf.TryGetValue(itemId, out var recipe) ? recipe : null;
}
