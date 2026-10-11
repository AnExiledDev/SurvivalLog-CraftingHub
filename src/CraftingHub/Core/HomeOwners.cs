using System.Collections.Generic;

namespace CraftingHub.Core;

public static class HomeOwners
{
    /// <summary>
    /// The home storages to add to the workbench's linked list: not already linked, not the bench itself, and not
    /// "no owner" (0), so the game never counts one storage's items twice.
    /// </summary>
    public static List<long> ToAdd(IReadOnlyCollection<long> linked, IEnumerable<long> home, IReadOnlyCollection<long> excluded)
    {
        var seen = new HashSet<long>(linked);
        var added = new List<long>();

        foreach (var owner in home)
        {
            if (owner == 0 || Contains(excluded, owner) || !seen.Add(owner)) continue;

            added.Add(owner);
        }

        return added;
    }

    private static bool Contains(IReadOnlyCollection<long> owners, long owner)
    {
        foreach (var candidate in owners)
        {
            if (candidate == owner) return true;
        }

        return false;
    }
}
