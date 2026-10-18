using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public static StaminaView? From(StaminaRead? read, IEnumerable<string> recipeKeys, bool hasQueue, bool hasStoppedForStamina = false)
    {
        if (read is not { } stamina || stamina.IsUnanswered) return null;

        var cost = stamina.Cost > 0 ? recipeKeys.Distinct().ToDictionary(key => key, _ => stamina.Cost) : new Dictionary<string, int>();

        var isOut = hasQueue && (stamina.IsShort || hasStoppedForStamina && !stamina.CoversACraft);

        return new StaminaView(stamina.Have, isOut, cost);
    }
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
        StaminaView? stamina = null)
    {
        var forecast = outlook ?? QueueOutlook.Empty;
        var markPairs = marks.OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{Str(pair.Key)}:{Str(pair.Value)}");
        var entries = queue.Select(entry => Entry(entry, forecast));
        var totals = forecast.Totals.Select(total => $"{{\"item\":{Num(total.ItemId)},\"name\":{Str(total.Name)},\"need\":{Num(total.Need)},\"have\":{Num(total.Have)},\"short\":{Num(total.Short)}}}");
        var yieldPairs = (yields ?? NoYields).Where(pair => pair.Value > 1).OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{Str(pair.Key)}:{Num(pair.Value)}");

        return new StringBuilder(2048)
            .Append("{\"marks\":{").AppendJoin(',', markPairs)
            .Append("},\"queue\":[").AppendJoin(',', entries)
            .Append("],\"running\":").Append(Bool(isRunning))
            .Append(",\"paused\":").Append(Bool(isRunning && isPaused))
            .Append(",\"activeId\":").Append(isRunning ? activeOrderId.ToString(CultureInfo.InvariantCulture) : "0")
            .Append(",\"status\":").Append(Str(status))
            .Append(",\"headsUp\":").Append(Str(forecast.HeadsUp))
            .Append(",\"totals\":[").AppendJoin(',', totals)
            .Append("],\"yields\":{").AppendJoin(',', yieldPairs)
            .Append("},\"keep\":").Append(Keep(keep))
            .Append(",\"stamina\":").Append(Stamina(stamina))
            .Append('}')
            .ToString();
    }

    /// <summary>An order with its id, for moving and resizing it, and where it stands; an order not costed yet has no state.</summary>
    private static string Entry(QueueEntry entry, QueueOutlook forecast)
    {
        var costed = forecast.Orders.Where(order => order.Id == entry.Id).Take(1).ToList();
        var state = costed.Count > 0 ? State(costed[0].State) : "";
        var note = costed.Count > 0 ? costed[0].Note : "";

        return $"{{\"id\":{entry.Id.ToString(CultureInfo.InvariantCulture)},\"key\":{Str(entry.Key)},\"name\":{Str(entry.Name)},\"crafts\":{Num(entry.Crafts)},\"topUp\":{Bool(entry.IsTopUp)},\"state\":{Str(state)},\"note\":{Str(note)}}}";
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

    private static string Keep(KeepView keep)
    {
        var targets = keep.Targets.Select(target => $"{{\"item\":{Num(target.ItemId)},\"key\":{Str(target.RecipeKey)},\"name\":{Str(target.Name)},\"count\":{Num(target.Count)},\"have\":{Have(keep, target.ItemId)}}}");
        var byKey = keep.ByKey.OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{Str(pair.Key)}:{Num(pair.Value)}");

        return new StringBuilder(256)
            .Append("{\"on\":").Append(Bool(keep.IsOn))
            .Append(",\"targets\":[").AppendJoin(',', targets)
            .Append("],\"byKey\":{").AppendJoin(',', byKey)
            .Append("}}")
            .ToString();
    }

    /// <summary>Null when stamina could not be read; a recipe that costs nothing is left out of the costs.</summary>
    private static string Stamina(StaminaView? stamina)
    {
        if (stamina is not { } shown) return "null";

        var costs = shown.Cost.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{Str(pair.Key)}:{Num(pair.Value)}");

        return new StringBuilder(128)
            .Append("{\"have\":").Append(Num(shown.Have))
            .Append(",\"out\":").Append(Bool(shown.IsOut))
            .Append(",\"cost\":{").AppendJoin(',', costs)
            .Append("}}")
            .ToString();
    }

    private static string Have(KeepView keep, int itemId) => keep.Have is { } have && have.TryGetValue(itemId, out var count) ? Num(count) : "null";

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>A JavaScript string literal, safe to paste into a script.</summary>
    public static string Str(string? text)
    {
        var literal = new StringBuilder((text?.Length ?? 0) + 2).Append('"');

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

        return literal.Append('"').ToString();
    }
}
