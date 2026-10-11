using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CraftingHub.Core;

/// <summary>The JSON the workbench page draws from: chain marks, the queue, and the run's status line.</summary>
public static class HubView
{
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    public static string Build(IReadOnlyDictionary<string, string> marks, IReadOnlyList<QueueEntry> queue, bool isRunning, string status)
    {
        var markPairs = marks.OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{Str(pair.Key)}:{Str(pair.Value)}");
        var entries = queue.Select(entry => $"{{\"key\":{Str(entry.Key)},\"name\":{Str(entry.Name)},\"crafts\":{entry.Crafts.ToString(CultureInfo.InvariantCulture)}}}");

        return new StringBuilder(1024)
            .Append("{\"marks\":{").AppendJoin(',', markPairs)
            .Append("},\"queue\":[").AppendJoin(',', entries)
            .Append("],\"running\":").Append(isRunning ? "true" : "false")
            .Append(",\"status\":").Append(Str(status))
            .Append('}')
            .ToString();
    }

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
