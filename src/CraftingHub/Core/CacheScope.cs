namespace CraftingHub.Core;

/// <summary>
/// The language and the save that names and recipes read from the game belong to. Names change with the language,
/// and anything read in one world is not trusted in the next, so either moving means what was kept must be read again.
/// </summary>
public sealed class CacheScope
{
    private bool _isChinese;
    private string? _save;
    private bool _hasScope;

    /// <summary>
    /// True when the language or the save moved since the last call. The save name can read as missing for a moment
    /// while a save loads, so a missing name is not a move.
    /// </summary>
    public bool Follow(bool isChinese, string? save)
    {
        var nextSave = save ?? _save;

        if (_hasScope && isChinese == _isChinese && nextSave == _save) return false;

        var hasMoved = _hasScope;

        _hasScope = true;
        _isChinese = isChinese;
        _save = nextSave;

        return hasMoved;
    }
}
