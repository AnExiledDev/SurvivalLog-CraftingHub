using System;
using System.IO;
using CraftingHub.Core;

namespace CraftingHub.Adapters;

/// <summary>
/// Reads and writes the keep-stocked targets file. The game's save has no room for a mod's data, so targets live in
/// one file beside the mod's config, keyed by save. A file that cannot be read is never written over, so a passing
/// lock or a bad line cannot wipe every other save's targets.
/// </summary>
internal sealed class KeepStore
{
    private readonly string _path;
    private readonly Action<string> _log;

    public KeepStore(string path, Action<string> log)
    {
        _path = path;
        _log = log;
    }

    public KeepTargets Load(string save) => ReadText() is { } text ? TargetFile.Read(text.Contents, save) : new KeepTargets();

    /// <summary>Writes this save's targets; false when the file could not be read or written.</summary>
    public bool Save(string save, KeepTargets targets)
    {
        if (ReadText() is not { } text) return false;

        var temp = _path + ".tmp";

        try
        {
            File.WriteAllText(temp, TargetFile.Write(text.Contents, save, targets));
            File.Move(temp, _path, overwrite: true);

            return true;
        }
        catch (Exception ex)
        {
            _log($"could not save the keep-stocked targets to {_path}: {ex.Message}");

            return false;
        }
    }

    /// <summary>The file's text, empty when there is no file yet, or null when it is there but cannot be read.</summary>
    private FileText? ReadText()
    {
        try
        {
            return new FileText(File.Exists(_path) ? File.ReadAllText(_path) : "");
        }
        catch (Exception ex)
        {
            _log($"could not read the keep-stocked targets from {_path}: {ex.Message}");

            return null;
        }
    }

    private sealed record FileText(string Contents);
}
