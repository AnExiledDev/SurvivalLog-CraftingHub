using System;
using System.Collections.Generic;

namespace CraftingHub.Core;

/// <summary>
/// One queued order: make <see cref="Crafts"/> more crafts of a recipe. <see cref="IsTopUp"/> marks an order keep-stocked
/// queued. <see cref="Id"/> tells orders apart, so finished crafts go to the order they were planned for.
/// </summary>
public readonly record struct QueueEntry(string Key, string Name, int Crafts, bool IsTopUp = false, long Id = 0);

/// <summary>The crafts waiting their turn, run head first. Kept in memory only, so a restart empties it.</summary>
public sealed class CraftQueue
{
    private readonly List<QueueEntry> _entries = new();
    private long _lastId;

    public IReadOnlyList<QueueEntry> Entries => _entries;

    public QueueEntry? Head => _entries.Count > 0 ? _entries[0] : null;

    /// <summary>
    /// Adds to the back, or tops up the last order when it is the same recipe from the same source. A top-up is never
    /// merged into the player's own order, since the two may use different stock.
    /// </summary>
    public void Add(string key, string name, int crafts, bool isTopUp = false)
    {
        if (crafts <= 0) return;

        if (_entries.Count > 0 && _entries[^1].Key == key && _entries[^1].IsTopUp == isTopUp)
        {
            _entries[^1] = _entries[^1] with { Crafts = _entries[^1].Crafts + crafts };

            return;
        }

        _entries.Add(new QueueEntry(key, name, crafts, isTopUp, ++_lastId));
    }

    /// <summary>
    /// Jumps the queue, for "craft this now" on the recipe the player is looking at. With <paramref name="behindHead"/>
    /// (a step of the head order is being made) it goes second, so the head stays the order that step counts against.
    /// </summary>
    public void PushFront(string key, string name, int crafts, bool behindHead = false)
    {
        if (crafts <= 0) return;

        var at = behindHead ? Math.Min(1, _entries.Count) : 0;

        if (at < _entries.Count && _entries[at].Key == key && !_entries[at].IsTopUp)
        {
            _entries[at] = _entries[at] with { Crafts = _entries[at].Crafts + crafts };

            return;
        }

        _entries.Insert(at, new QueueEntry(key, name, crafts, Id: ++_lastId));
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _entries.Count) _entries.RemoveAt(index);
    }

    public void Clear() => _entries.Clear();

    /// <summary>
    /// Counts finished crafts against the order they were planned for and drops it once none are left. An order that
    /// is no longer queued gets nothing; no other order is credited in its place.
    /// </summary>
    public void Progress(long orderId, int crafts)
    {
        var index = _entries.FindIndex(entry => entry.Id == orderId);

        if (index < 0 || crafts <= 0) return;

        var left = _entries[index].Crafts - crafts;

        if (left > 0)
        {
            _entries[index] = _entries[index] with { Crafts = left };

            return;
        }

        _entries.RemoveAt(index);
    }
}
