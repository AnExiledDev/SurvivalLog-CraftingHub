using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftingHub.Core;

public enum OrderState
{
    Ready,
    MakesFirst,
    Short,
    Locked,
    NotListed,
}

/// <summary>Where one queued order stands, with the line the queue shows under it.</summary>
public readonly record struct OrderOutlook(long Id, OrderState State, string Note);

/// <summary>One material the whole queue uses up: how many it takes from what you own, how many you own, and how many are missing.</summary>
public readonly record struct MaterialTotal(int ItemId, string Name, int Need, int Have, int Short);

/// <summary>An intermediate the queue crafts on its way to the orders.</summary>
public readonly record struct MadeOnTheWay(string Key, string Name, int Crafts);

public sealed record QueueOutlook(IReadOnlyList<OrderOutlook> Orders, IReadOnlyList<MaterialTotal> Totals, IReadOnlyList<MadeOnTheWay> Made, string HeadsUp)
{
    public static readonly QueueOutlook Empty = new(Array.Empty<OrderOutlook>(), Array.Empty<MaterialTotal>(), Array.Empty<MadeOnTheWay>(), "");
}

/// <summary>
/// Costs the whole queue before it runs, top to bottom, as if every order were made in turn: each order plans from
/// what the orders above it leave behind, the same way the run will. A short order still takes what it can, so the
/// totals show everything the queue would need, and the first order that cannot be made is named before Start.
/// </summary>
public static class QueueForecast
{
    public static QueueOutlook Plan(IReadOnlyList<QueueEntry> queue, RecipeBook book, IReadOnlyDictionary<int, int> stock, IReadOnlyList<KeepTarget> kept, int maxDepth, Func<int, string> itemName)
    {
        var walk = new QueueWalk(book, stock, kept, maxDepth, itemName);
        var orders = queue.Select(walk.Order).ToList();
        var headsUp = queue.Zip(orders, HeadsUp).FirstOrDefault(line => line.Length > 0) ?? "";

        return new QueueOutlook(orders, walk.Totals(), walk.Made, headsUp);
    }

    private const string ShortPrefix = "Short: ";

    private static string HeadsUp(QueueEntry order, OrderOutlook outlook) => outlook.State switch
    {
        OrderState.Short => $"Heads up: {order.Name} is short of {outlook.Note.Substring(ShortPrefix.Length)}. The queue will stop there.",
        OrderState.Locked => $"Heads up: {order.Name} is not unlocked yet. The queue will stop there.",
        OrderState.NotListed => $"Heads up: {order.Name} is not in your recipe list yet. The queue will stop there.",
        _ => "",
    };

    private sealed class QueueWalk
    {
        private readonly RecipeBook _book;
        private readonly IReadOnlyDictionary<int, int> _stock;
        private readonly IReadOnlyList<KeepTarget> _kept;
        private readonly int _maxDepth;
        private readonly Func<int, string> _itemName;
        private readonly Dictionary<int, int> _left;
        private readonly Dictionary<int, int> _missing = new();
        private readonly List<MadeOnTheWay> _made = new();

        public QueueWalk(RecipeBook book, IReadOnlyDictionary<int, int> stock, IReadOnlyList<KeepTarget> kept, int maxDepth, Func<int, string> itemName)
        {
            _book = book;
            _stock = stock;
            _kept = kept;
            _maxDepth = maxDepth;
            _itemName = itemName;
            _left = stock.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        public IReadOnlyList<MadeOnTheWay> Made => _made;

        public OrderOutlook Order(QueueEntry order)
        {
            if (_book.Find(order.Key) is not { } target) return new OrderOutlook(order.Id, OrderState.NotListed, "Not in your recipe list yet");

            if (target.IsLocked) return new OrderOutlook(order.Id, OrderState.Locked, "Not unlocked yet");

            // A top-up only plans from what is spare above the kept counts, as the run does.
            var floor = order.IsTopUp ? KeptStock.Floor(_kept) : null;
            var walk = CraftPlanner.Walk(_book, _left, target, order.Crafts, _maxDepth, floor);
            var intermediates = Merged(walk.Steps.Take(walk.Steps.Count - 1));

            TakeFromStock(walk);
            Count(walk.Missing);
            foreach (var step in intermediates) AddTo(_made, step);

            if (walk.Missing.Count > 0) return new OrderOutlook(order.Id, OrderState.Short, ShortPrefix + Missing(walk.Missing));

            if (intermediates.Count > 0) return new OrderOutlook(order.Id, OrderState.MakesFirst, "Makes first: " + string.Join(", ", intermediates.Select(step => $"{step.Name} x{step.Crafts}")));

            return new OrderOutlook(order.Id, OrderState.Ready, "Ready");
        }

        /// <summary>
        /// Net use against what you own, short items first. Something the queue makes and then uses up again does not
        /// show; it is in <see cref="Made"/>.
        /// </summary>
        public IReadOnlyList<MaterialTotal> Totals() =>
            _stock.Keys.Union(_missing.Keys)
                .Select(Total)
                .Where(total => total.Need > 0)
                .OrderBy(total => total.Short > 0 ? 0 : 1)
                .ThenBy(total => total.Name, StringComparer.Ordinal)
                .ThenBy(total => total.ItemId)
                .ToList();

        private MaterialTotal Total(int itemId)
        {
            var have = _stock.GetValueOrDefault(itemId);
            var missing = _missing.GetValueOrDefault(itemId);
            var used = Math.Max(0, have - _left.GetValueOrDefault(itemId));

            return new MaterialTotal(itemId, _itemName(itemId), used + missing, have, missing);
        }

        /// <summary>The walk planned from everything left, so what it leaves is what the next order starts from.</summary>
        private void TakeFromStock(CraftWalk walk)
        {
            foreach (var (itemId, after) in walk.Left) _left[itemId] = after;
        }

        private void Count(IReadOnlyDictionary<int, int> missing)
        {
            foreach (var (itemId, amount) in missing) _missing[itemId] = _missing.GetValueOrDefault(itemId) + amount;
        }

        private string Missing(IReadOnlyDictionary<int, int> missing) =>
            string.Join(", ", missing.OrderBy(pair => pair.Key).Select(pair => $"{_itemName(pair.Key)} x{pair.Value}"));

        private static List<MadeOnTheWay> Merged(IEnumerable<CraftStep> steps)
        {
            var merged = new List<MadeOnTheWay>();

            foreach (var step in steps) AddTo(merged, new MadeOnTheWay(step.Recipe.Key, step.Recipe.Name, step.Crafts));

            return merged;
        }

        /// <summary>Adds crafts to the recipe's entry, so a recipe made at several points shows once, where it first appears.</summary>
        private static void AddTo(List<MadeOnTheWay> made, MadeOnTheWay step)
        {
            var index = made.FindIndex(entry => entry.Key == step.Key);

            if (index >= 0) made[index] = made[index] with { Crafts = made[index].Crafts + step.Crafts };
            else made.Add(step);
        }
    }
}
