using System;
using System.Globalization;

namespace CraftingHub.Core;

/// <summary>
/// What the workbench page asks for. The page sends "slmod|craftinghub|" then a verb and its arguments split by '|';
/// recipe keys are URI-encoded so a '|' inside one cannot split it. Anything else parses to Unknown.
/// </summary>
public abstract record PageCommand
{
    public const string Prefix = "slmod|craftinghub|";

    private const string DiagnosticVerb = "diag|";

    private PageCommand()
    {
    }

    /// <summary>Queue a recipe at the back.</summary>
    public sealed record Add(string Key, int Crafts) : PageCommand;

    /// <summary>Craft a recipe now, making its intermediates first.</summary>
    public sealed record CraftNow(string Key, int Crafts) : PageCommand;

    public sealed record Start : PageCommand;

    public sealed record Stop : PageCommand;

    public sealed record Remove(int Index) : PageCommand;

    public sealed record Clear : PageCommand;

    /// <summary>Move the order with this id to place <see cref="To"/> in the queue, 0 being the front.</summary>
    public sealed record Move(long Id, int To) : PageCommand;

    /// <summary>Set how many crafts the order with this id still wants.</summary>
    public sealed record Amount(long Id, int Crafts) : PageCommand;

    /// <summary>Keep <see cref="Count"/> of what a recipe makes in the home; 0 stops keeping it.</summary>
    public sealed record Keep(string Key, int Count) : PageCommand;

    /// <summary>Stop keeping an item stocked.</summary>
    public sealed record Unkeep(int ItemId) : PageCommand;

    /// <summary>The page script just installed itself.</summary>
    public sealed record Ready : PageCommand;

    /// <summary>The page has no script, usually because the web view reloaded.</summary>
    public sealed record NoScript : PageCommand;

    /// <summary>Something the page could not do, for the log.</summary>
    public sealed record Diagnostic(string Text) : PageCommand;

    public sealed record Unknown(string Text) : PageCommand;

    public static bool IsOurs(string? message) => message is not null && message.StartsWith(Prefix, StringComparison.Ordinal);

    public static PageCommand Parse(string message)
    {
        if (!IsOurs(message)) return new Unknown(message);

        var body = message.Substring(Prefix.Length);

        if (body.StartsWith(DiagnosticVerb, StringComparison.Ordinal)) return new Diagnostic(body.Substring(DiagnosticVerb.Length));

        var parts = body.Split('|');

        return (parts[0], parts.Length) switch
        {
            ("add", 3) when TryKey(parts[1], out var key) && TryCount(parts[2], out var crafts) => new Add(key, crafts),
            ("chain", 3) when TryKey(parts[1], out var key) && TryCount(parts[2], out var crafts) => new CraftNow(key, crafts),
            ("start", 1) => new Start(),
            ("stop", 1) => new Stop(),
            ("remove", 2) when TryInt(parts[1], out var index) => new Remove(index),
            ("clear", 1) => new Clear(),
            ("move", 3) when TryId(parts[1], out var id) && TryInt(parts[2], out var to) => new Move(id, to),
            ("amount", 3) when TryId(parts[1], out var id) && TryCount(parts[2], out var crafts) => new Amount(id, crafts),
            ("keep", 3) when TryKey(parts[1], out var key) && TryInt(parts[2], out var count) && count <= KeepTarget.MaxCount => new Keep(key, count),
            ("unkeep", 2) when TryCount(parts[1], out var itemId) => new Unkeep(itemId),
            ("ready", 1) => new Ready(),
            ("noscript", 1) => new NoScript(),
            _ => new Unknown(message),
        };
    }

    private static bool TryKey(string text, out string key)
    {
        try
        {
            key = Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            key = "";
        }

        return key.Length > 0;
    }

    private static bool TryCount(string text, out int count) => TryInt(text, out count) && count > 0;

    private static bool TryInt(string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryId(string text, out long id) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
