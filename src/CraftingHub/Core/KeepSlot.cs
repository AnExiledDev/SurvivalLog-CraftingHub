using System;

namespace CraftingHub.Core;

/// <summary>What reading one save's targets gave: its targets, or a targets file that is there but could not be read.</summary>
public abstract record TargetLoad
{
    private TargetLoad()
    {
    }

    public sealed record Loaded(KeepTargets Targets) : TargetLoad;

    public sealed record Unreadable : TargetLoad;
}

/// <summary>Where targets are kept between game sessions.</summary>
public interface ITargetStore
{
    TargetLoad Load(string save);

    /// <summary>Writes this save's targets, leaving every other save's; false when that failed.</summary>
    bool Save(string save, KeepTargets targets);
}

public enum SlotLoad
{
    NoSave,
    AlreadyRead,
    Read,
    Unreadable,
}

public enum KeepEdit
{
    Saved,
    NotSaved,
    NoSave,
    Unreadable,
}

/// <summary>
/// The targets of the save the game has loaded. They are read once per save. A targets file that could not be read
/// is tried again on the next load, and until it reads no target can be changed: a write then would replace the
/// save's targets with only the one just set.
/// </summary>
public sealed class KeepSlot
{
    private readonly ITargetStore _store;
    private string? _save;
    private bool _isRead;

    public KeepSlot(ITargetStore store) => _store = store;

    public KeepTargets Targets { get; private set; } = new();

    public string? Save => _save;

    /// <summary>Follows the save the game has loaded; true when it moved from one save to another.</summary>
    public bool Follow(string? save)
    {
        if (save == _save) return false;

        var isOtherSave = _save != null;
        _save = save;
        _isRead = false;
        Targets = new KeepTargets();

        return isOtherSave;
    }

    /// <summary>Reads the save's targets unless they are already in.</summary>
    public SlotLoad Load()
    {
        if (_save is not { } save) return SlotLoad.NoSave;

        if (_isRead) return SlotLoad.AlreadyRead;

        if (_store.Load(save) is not TargetLoad.Loaded loaded) return SlotLoad.Unreadable;

        Targets = loaded.Targets;
        _isRead = true;

        return SlotLoad.Read;
    }

    public KeepEdit Set(KeepTarget target) => Edit(targets => targets.Set(target));

    public KeepEdit Remove(int itemId) => Edit(targets => targets.Remove(itemId));

    /// <summary>A change the store could not write stays in memory for this session, so the status line can say so.</summary>
    private KeepEdit Edit(Action<KeepTargets> change)
    {
        if (_save is not { } save) return KeepEdit.NoSave;

        if (!_isRead) return KeepEdit.Unreadable;

        change(Targets);

        return _store.Save(save, Targets) ? KeepEdit.Saved : KeepEdit.NotSaved;
    }
}
