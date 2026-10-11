using System.Collections.Generic;

namespace CraftingHub.Core;

/// <summary>One queued order: make <see cref="Crafts"/> more crafts of a recipe.</summary>
public readonly record struct QueueEntry(string Key, string Name, int Crafts);

/// <summary>The crafts waiting their turn, run head first. Kept in memory only, so a restart empties it.</summary>
public sealed class CraftQueue
{
    private readonly List<QueueEntry> _entries = new();

    public IReadOnlyList<QueueEntry> Entries => _entries;

    public QueueEntry? Head => _entries.Count > 0 ? _entries[0] : null;

    /// <summary>Adds to the back, or tops up the last order when it is the same recipe.</summary>
    public void Add(string key, string name, int crafts)
    {
        if (crafts <= 0) return;

        if (_entries.Count > 0 && _entries[^1].Key == key)
        {
            _entries[^1] = _entries[^1] with { Crafts = _entries[^1].Crafts + crafts };

            return;
        }

        _entries.Add(new QueueEntry(key, name, crafts));
    }

    /// <summary>Jumps the queue, for "craft this now" on the recipe the player is looking at.</summary>
    public void PushFront(string key, string name, int crafts)
    {
        if (crafts <= 0) return;

        if (_entries.Count > 0 && _entries[0].Key == key)
        {
            _entries[0] = _entries[0] with { Crafts = _entries[0].Crafts + crafts };

            return;
        }

        _entries.Insert(0, new QueueEntry(key, name, crafts));
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _entries.Count) _entries.RemoveAt(index);
    }

    public void Clear() => _entries.Clear();

    /// <summary>Counts finished crafts against the head order and drops it once none are left.</summary>
    public void Progress(int crafts)
    {
        if (_entries.Count == 0 || crafts <= 0) return;

        var left = _entries[0].Crafts - crafts;

        if (left > 0)
        {
            _entries[0] = _entries[0] with { Crafts = left };

            return;
        }

        _entries.RemoveAt(0);
    }
}
