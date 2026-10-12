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
/// The JSON the workbench page draws from: chain marks, the queue with where each order stands, the run's state and
/// status line, the order the craft in progress is for, what the whole queue uses and makes on the way, how many each
/// recipe makes, and the keep-stocked targets.
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
        long activeOrderId = 0)
    {
        var forecast = outlook ?? QueueOutlook.Empty;
        var markPairs = marks.OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{Str(pair.Key)}:{Str(pair.Value)}");
        var entries = queue.Select(entry => Entry(entry, forecast));
        var totals = forecast.Totals.Select(total => $"{{\"item\":{Num(total.ItemId)},\"name\":{Str(total.Name)},\"need\":{Num(total.Need)},\"have\":{Num(total.Have)},\"short\":{Num(total.Short)}}}");
        var made = forecast.Made.Select(step => $"{{\"key\":{Str(step.Key)},\"name\":{Str(step.Name)},\"crafts\":{Num(step.Crafts)}}}");
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
            .Append("],\"made\":[").AppendJoin(',', made)
            .Append("],\"yields\":{").AppendJoin(',', yieldPairs)
            .Append("},\"keep\":").Append(Keep(keep))
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
