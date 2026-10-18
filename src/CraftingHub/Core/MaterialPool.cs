using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

/// <summary>
/// The game's "can this recipe be crafted" test, answered from one look at the storages. The game regroups every item
/// in every storage for each recipe it lists; this groups them once per recipe-list rebuild and answers every recipe
/// from that. The rule is the game's own: a material's stacks are tried largest first, a stack counts in full or not
/// at all (a stack larger than what is still needed is passed over), and a stack of zero or less counts as one.
/// </summary>
public sealed class MaterialPool
{
    /// <summary>Per item id, the stack sizes in the order the game tries them.</summary>
    private readonly Dictionary<int, int[]> _stacks;

    private MaterialPool(Dictionary<int, int[]> stacks)
    {
        _stacks = stacks;
    }

    /// <summary>Every stack in the storages, as item id and stack count; a storage listed twice is listed twice.</summary>
    public static MaterialPool From(IEnumerable<(int ItemId, int Count)> stacks)
    {
        var tried = stacks
            .GroupBy(stack => stack.ItemId)
            .ToDictionary(group => group.Key, group => group.Select(stack => stack.Count).OrderByDescending(count => count).Select(CountedAs).ToArray());

        return new MaterialPool(tried);
    }

    /// <summary>
    /// Whether the storages cover every material. Like the game, an empty pool covers nothing, and a material with no
    /// stack at all fails even when none of it is needed.
    /// </summary>
    public bool Covers(IEnumerable<KeyValuePair<int, int>> needed)
    {
        if (_stacks.Count == 0) return false;

        foreach (var material in needed)
        {
            if (!_stacks.TryGetValue(material.Key, out var stacks) || !Fills(stacks, material.Value)) return false;
        }

        return true;
    }

    private static bool Fills(int[] stacks, int needed)
    {
        var left = needed;

        foreach (var stack in stacks)
        {
            if (left <= 0) break;

            if (stack <= left) left -= stack;
        }

        return left <= 0;
    }

    private static int CountedAs(int count) => count <= 0 ? 1 : count;
}
