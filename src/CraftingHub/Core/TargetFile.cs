using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CraftingHub.Core;

/// <summary>
/// The keep-stocked targets file: one tab-separated line per target, "save, item id, count, recipe key, name", for
/// every save at once. The save, the key and the name are URI-escaped so a tab or a line break in one cannot split
/// a line. Writing one save's targets keeps every other line (other saves, comments) exactly as it was.
/// </summary>
public static class TargetFile
{
    public const string Header = "# Crafting Hub keep-stocked targets. One line per target: save, item id, count, recipe key, name.";

    private const char Separator = '\t';

    public static KeepTargets Read(string? text, string save)
    {
        var targets = new KeepTargets();

        foreach (var line in Lines(text))
        {
            if (Parse(line) is { } parsed && parsed.Save == save) targets.Set(parsed.Target);
        }

        return targets;
    }

    public static string Write(string? text, string save, KeepTargets targets)
    {
        var file = new StringBuilder().Append(Header).Append('\n');

        foreach (var line in Lines(text).Where(line => !IsSave(line, save))) file.Append(line).Append('\n');

        foreach (var target in targets.All) file.Append(Format(save, target)).Append('\n');

        return file.ToString();
    }

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? "").Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0 && line != Header);

    private static bool IsSave(string line, string save) => Parse(line) is { } parsed && parsed.Save == save;

    private static string Format(string save, KeepTarget target) =>
        string.Join(Separator, Uri.EscapeDataString(save), target.ItemId.ToString(CultureInfo.InvariantCulture),
            target.Count.ToString(CultureInfo.InvariantCulture), Uri.EscapeDataString(target.RecipeKey), Uri.EscapeDataString(target.Name));

    private static (string Save, KeepTarget Target)? Parse(string line)
    {
        var fields = line.Split(Separator);

        if (line.StartsWith('#') || fields.Length != 5) return null;

        if (!TryInt(fields[1], out var itemId) || !TryInt(fields[2], out var count) || count <= 0) return null;

        if (Unescape(fields[0]) is not { } save || Unescape(fields[3]) is not { Length: > 0 } key || Unescape(fields[4]) is not { } name) return null;

        return (save, new KeepTarget(itemId, key, name, count));
    }

    private static string? Unescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static bool TryInt(string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
