using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CraftingHub.Core;

/// <summary>
/// What the page shows of the keep-stocked targets: all of this save's, the count kept per recipe key, and how many of
/// each kept item the home holds (absent when the game's items could not be counted).
/// </summary>
public readonly record struct KeepView(bool IsOn, IReadOnlyList<KeepTarget> Targets, IReadOnlyDictionary<string, int> ByKey, IReadOnlyDictionary<int, int>? Have = null);

/// <summary>
/// The player's stamina for the page: how much they have, whether it holds the queue up, and the stamina one craft of
/// each listed recipe costs.
/// </summary>
public readonly record struct StaminaView(int Have, bool IsOut, IReadOnlyDictionary<string, int> Cost)
{
    /// <summary>
    /// The game works a craft's stamina cost out from the player, not the recipe, so every listed recipe gets the same
    /// cost. Out only while the queue waits on a craft the player cannot afford now, so it clears as stamina comes
    /// back. After a run stopped for stamina the game may report no cost at all, so the warning then holds until the
    /// player can be seen to cover a craft or starts the queue again. Null when stamina could not be read.
    /// </summary>
    public static StaminaView? From(StaminaRead? read, IEnumerable<string> recipeKeys, bool hasQueue, bool hasStoppedForStamina = false) =>
        StaminaGist.Of(read, hasQueue, hasStoppedForStamina)?.For(recipeKeys);
}

/// <summary>
/// The JSON the workbench page draws from: chain marks, the queue with where each order stands, the run's state and
/// status line, the order the craft in progress is for, what the whole queue uses, how many each
/// recipe makes, the keep-stocked targets, and the player's stamina.
/// </summary>
public static class HubView
{
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    private static readonly IReadOnlyDictionary<string, int> NoYields = new Dictionary<string, int>();
    private static readonly IReadOnlyList<DyeOption> NoDyes = new List<DyeOption>();

    /// <summary>Written into in place, so a view costs one string and not one per field.</summary>
    [ThreadStatic] private static StringBuilder? _json;

    public static string Build(
        IReadOnlyDictionary<string, string> marks,
        IReadOnlyList<QueueEntry> queue,
        bool isRunning,
        string status,
        KeepView keep,
        bool isPaused = false,
        QueueOutlook? outlook = null,
        IReadOnlyDictionary<string, int>? yields = null,
        long activeOrderId = 0,
        StaminaView? stamina = null,
        bool takesUnitsOffStacks = false,
        IReadOnlyList<DyeOption>? dyes = null)
    {
        var forecast = outlook ?? QueueOutlook.Empty;
        var json = _json ??= new StringBuilder(4096);
        var isFirst = true;

        json.Clear().Append("{\"marks\":{");

        foreach (var key in SortedKeys(marks))
        {
            Comma(json, ref isFirst);
            Str(json, key).Append(':');
            Str(json, marks[key]);
        }

        json.Append("},\"queue\":[");
        isFirst = true;

        foreach (var entry in queue)
        {
            Comma(json, ref isFirst);
            Entry(json, entry, forecast.Orders);
        }

        json.Append("],\"running\":").Append(Bool(isRunning))
            .Append(",\"paused\":").Append(Bool(isRunning && isPaused))
            .Append(",\"activeId\":");
        Num(json, isRunning ? activeOrderId : 0).Append(",\"status\":");
        Str(json, status).Append(",\"headsUp\":");
        Str(json, forecast.HeadsUp).Append(",\"totals\":[");
        isFirst = true;

        foreach (var total in forecast.Totals)
        {
            Comma(json, ref isFirst);
            Num(json.Append("{\"item\":"), total.ItemId).Append(",\"name\":");
            Str(json, total.Name).Append(",\"need\":");
            Num(json, total.Need).Append(",\"have\":");
            Num(json, total.Have).Append(",\"short\":");
            Num(json, total.Short).Append('}');
        }

        json.Append("],\"yields\":{");
        Counts(json, yields ?? NoYields, minimum: 2);
        json.Append("},\"keep\":");
        Keep(json, keep);
        json.Append(",\"stamina\":");
        Stamina(json, stamina);
        json.Append(",\"takesUnits\":").Append(Bool(takesUnitsOffStacks)).Append(",\"dyes\":");
        Dyes(json, dyes ?? NoDyes);

        return json.Append('}').ToString();
    }

    /// <summary>An order with its id, for moving and resizing it, and where it stands; an order not costed yet has no state.</summary>
    private static void Entry(StringBuilder json, QueueEntry entry, IReadOnlyList<OrderOutlook> orders)
    {
        var state = "";
        var note = "";

        for (var i = 0; i < orders.Count; i++)
        {
            if (orders[i].Id != entry.Id) continue;

            state = State(orders[i].State);
            note = orders[i].Note;
            break;
        }

        Num(json.Append("{\"id\":"), entry.Id).Append(",\"key\":");
        Str(json, entry.Key).Append(",\"name\":");
        Str(json, entry.Name).Append(",\"crafts\":");
        Num(json, entry.Crafts).Append(",\"topUp\":").Append(Bool(entry.IsTopUp)).Append(",\"state\":");
        Str(json, state).Append(",\"note\":");
        Str(json, note).Append('}');
    }

    private static string State(OrderState state) => state switch
    {
        OrderState.Ready => "ready",
        OrderState.MakesFirst => "via",
        OrderState.Short => "short",
        OrderState.Locked => "locked",
        OrderState.NotListed => "unlisted",
        _ => "",
    };

    private static void Keep(StringBuilder json, KeepView keep)
    {
        var isFirst = true;

        json.Append("{\"on\":").Append(Bool(keep.IsOn)).Append(",\"targets\":[");

        foreach (var target in keep.Targets)
        {
            Comma(json, ref isFirst);
            Num(json.Append("{\"item\":"), target.ItemId).Append(",\"key\":");
            Str(json, target.RecipeKey).Append(",\"name\":");
            Str(json, target.Name).Append(",\"count\":");
            Num(json, target.Count).Append(",\"have\":");

            if (keep.Have is { } have && have.TryGetValue(target.ItemId, out var count)) Num(json, count);
            else json.Append("null");

            json.Append('}');
        }

        json.Append("],\"byKey\":{");
        Counts(json, keep.ByKey, minimum: int.MinValue);
        json.Append("}}");
    }

    /// <summary>The colors of each dyeable recipe, by its plain key, in the order the game's dye bar lists them.</summary>
    private static void Dyes(StringBuilder json, IReadOnlyList<DyeOption> dyes)
    {
        var bases = new HashSet<string>();

        foreach (var dye in dyes) bases.Add(dye.BaseKey);

        var sorted = new string[bases.Count];
        bases.CopyTo(sorted);
        Array.Sort(sorted, StringComparer.Ordinal);

        var isFirstGroup = true;

        json.Append('{');

        foreach (var baseKey in sorted)
        {
            Comma(json, ref isFirstGroup);
            Str(json, baseKey).Append(":[");

            var isFirst = true;

            foreach (var dye in dyes)
            {
                if (dye.BaseKey != baseKey) continue;

                Comma(json, ref isFirst);
                Str(json.Append("{\"key\":"), dye.Key).Append(",\"dye\":");
                Num(json, dye.DyeId).Append(",\"hex\":");
                Str(json, dye.Hex).Append(",\"color\":");
                Str(json, dye.Color).Append(",\"need\":");
                Num(json, dye.Need).Append(",\"have\":");
                Num(json, dye.Have).Append('}');
            }

            json.Append(']');
        }

        json.Append('}');
    }

    /// <summary>Null when stamina could not be read; a recipe that costs nothing is left out of the costs.</summary>
    private static void Stamina(StringBuilder json, StaminaView? stamina)
    {
        if (stamina is not { } shown)
        {
            json.Append("null");

            return;
        }

        Num(json.Append("{\"have\":"), shown.Have).Append(",\"out\":").Append(Bool(shown.IsOut)).Append(",\"cost\":{");
        Counts(json, shown.Cost, minimum: 1);
        json.Append("}}");
    }

    /// <summary>Key to count pairs in key order, leaving out any count below <paramref name="minimum"/>.</summary>
    private static void Counts(StringBuilder json, IReadOnlyDictionary<string, int> counts, int minimum)
    {
        var isFirst = true;

        foreach (var key in SortedKeys(counts))
        {
            var count = counts[key];

            if (count < minimum) continue;

            Comma(json, ref isFirst);
            Str(json, key).Append(':');
            Num(json, count);
        }
    }

    private static string[] SortedKeys<T>(IReadOnlyDictionary<string, T> map)
    {
        var keys = new string[map.Count];
        var at = 0;

        foreach (var pair in map) keys[at++] = pair.Key;

        Array.Sort(keys, StringComparer.Ordinal);

        return keys;
    }

    private static void Comma(StringBuilder json, ref bool isFirst)
    {
        if (!isFirst) json.Append(',');

        isFirst = false;
    }

    private static StringBuilder Num(StringBuilder json, long value) => json.Append(CultureInfo.InvariantCulture, $"{value}");

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>A JavaScript string literal, safe to paste into a script.</summary>
    public static string Str(string? text) => Str(new StringBuilder((text?.Length ?? 0) + 2), text).ToString();

    private static StringBuilder Str(StringBuilder literal, string? text)
    {
        literal.Append('"');

        foreach (var character in text ?? "")
        {
            switch (character)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                // Line and paragraph separators end a line in older JavaScript, and '<' keeps "</script>" inert.
                case < ' ' or LineSeparator or ParagraphSeparator or '<':
                    literal.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    literal.Append(character);
                    break;
            }
        }

        return literal.Append('"');
    }
}
