#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>What the player drags onto a building (a seed bag, the sickle, the collect hand, feed, a machine input).</summary>
    public enum ToolKind
    {
        /// <summary>Plant <see cref="Tool.Item"/> (a crop id) on an empty field.</summary>
        Seed,
        /// <summary>Harvest a ripe field into the farmhouse.</summary>
        Sickle,
        /// <summary>Collect what an animal house or a machine has made into the farmhouse.</summary>
        Hand,
        /// <summary>Put <see cref="Tool.Item"/> from the farmhouse into an animal house's trough.</summary>
        Feed,
        /// <summary>Put <see cref="Tool.Item"/> from the farmhouse into a machine's hopper.</summary>
        Input,
    }

    /// <summary>One draggable tool: its kind and, for seeds, feed and inputs, the item.</summary>
    public readonly struct Tool : IEquatable<Tool>
    {
        public Tool(ToolKind kind, string item = "")
        {
            Kind = kind;
            Item = item;
        }

        public ToolKind Kind { get; }
        public string Item { get; }

        public static Tool Seed(string crop) => new Tool(ToolKind.Seed, crop);
        public static readonly Tool Sickle = new Tool(ToolKind.Sickle);
        public static readonly Tool Hand = new Tool(ToolKind.Hand);
        public static Tool FeedWith(string item) => new Tool(ToolKind.Feed, item);
        public static Tool InputOf(string item) => new Tool(ToolKind.Input, item);

        public bool Equals(Tool other) => Kind == other.Kind && Item == other.Item;
        public override bool Equals(object? obj) => obj is Tool t && Equals(t);
        public override int GetHashCode() => ((int)Kind * 397) ^ Item.GetHashCode();
        public override string ToString() => Item.Length > 0 ? $"{Kind}:{Item}" : Kind.ToString();
    }

    /// <summary>Why a tool can't be used on a building right now (or <see cref="Ok"/>).</summary>
    public enum ToolRefusal
    {
        Ok,
        /// <summary>This building doesn't take this tool at all (a seed on a coop).</summary>
        WrongTarget,
        /// <summary>Not working: under construction, ruined, or a department without workers.</summary>
        NotWorking,
        /// <summary>A field already growing something; nothing ripe to harvest; nothing made yet.</summary>
        NotReady,
        /// <summary>The crop isn't unlocked at this island level.</summary>
        Locked,
        NoCoins,
        /// <summary>The trough or hopper is full.</summary>
        Full,
        /// <summary>None of that item in the farmhouse.</summary>
        NoStock,
        /// <summary>No room left in the farmhouse.</summary>
        StorageFull,
    }

    /// <summary>What using a tool did: how many units moved (0 when refused) and why not.</summary>
    public readonly struct ToolUse
    {
        public ToolUse(ToolRefusal refusal, string item = "", int units = 0)
        {
            Refusal = refusal;
            Item = item;
            Units = units;
        }

        public ToolRefusal Refusal { get; }
        /// <summary>The good that moved (the crop harvested, the feed tipped in…).</summary>
        public string Item { get; }
        public int Units { get; }
        public bool Ok => Refusal == ToolRefusal.Ok;
    }

    /// <summary>
    /// Drag-and-drop play (docs: Interaction): tools used on a building from anywhere, no farmer
    /// needed. Goods come from and go to the farmhouse store (its capacity applies), so these are a
    /// shortcut beside the farmer's own walk-in work, which still fills and empties the basket.
    /// </summary>
    public sealed partial class IslandEconomy
    {
        /// <summary>Room left in working farmhouses.</summary>
        public int FarmhouseRoom =>
            _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)).Sum(d => Math.Max(0, d.StorageCapacity - d.Stored));

        /// <summary>Stores up to <paramref name="n"/> into working farmhouses with room; returns how many fit.</summary>
        private int StoreInFarmhouses(string item, int n)
        {
            var left = n;
            foreach (var d in _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse && Working(d.Uid)).OrderBy(d => d.Uid))
            {
                var fit = Math.Min(left, Math.Max(0, d.StorageCapacity - d.Stored));
                if (fit <= 0) continue;
                d.Store(item, fit);
                left -= fit;
                if (left == 0) break;
            }
            return n - left;
        }

        /// <summary>The tools that make sense on a building now (the tray shown when it's tapped).</summary>
        public IReadOnlyList<Tool> ToolsFor(int uid, double now)
        {
            var tools = new List<Tool>();
            if (_plots.TryGetValue(uid, out var plot))
            {
                if (plot.State(now) == PlotState.Empty)
                    tools.AddRange(GameplayRules.Crops.Where(c => CropOpen(c.Id)).Select(c => Tool.Seed(c.Id)));
                else if (plot.State(now) == PlotState.Ripe) tools.Add(Tool.Sickle);
            }
            else if (_animals.TryGetValue(uid, out var a))
            {
                tools.Add(Tool.Hand);
                if (a.Producer.Eats)
                    tools.AddRange(StockedFeeds().Select(Tool.FeedWith));
            }
            else if (_machines.TryGetValue(uid, out var m))
            {
                tools.Add(Tool.Hand);
                tools.AddRange(m.Recipe.Inputs.Select(i => Tool.InputOf(i.item)));
            }
            return tools;
        }

        /// <summary>Feeds the farmhouses hold, best first.</summary>
        private IEnumerable<string> StockedFeeds() =>
            _departments.Values.Where(d => d.Levels.Kind == DepartmentKind.Farmhouse)
                .SelectMany(d => d.Stock.Keys).Distinct()
                .Where(k => GameplayRules.FeedValue(k) > 0)
                .OrderByDescending(GameplayRules.FeedValue).ThenBy(k => k, StringComparer.Ordinal);

        /// <summary>Whether <paramref name="tool"/> would do something on this building now (the drag highlight), without changing anything.</summary>
        public ToolRefusal CanUse(int uid, Tool tool, double now)
        {
            switch (tool.Kind)
            {
                case ToolKind.Seed:
                {
                    if (!_plots.TryGetValue(uid, out var plot)) return ToolRefusal.WrongTarget;
                    if (!Working(uid)) return ToolRefusal.NotWorking;
                    if (plot.State(now) != PlotState.Empty) return ToolRefusal.NotReady;
                    var crop = GameplayRules.CropOf(tool.Item);
                    if (crop == null) return ToolRefusal.WrongTarget;
                    if (!CropOpen(crop.Id)) return ToolRefusal.Locked;
                    return Coins >= crop.SeedCost ? ToolRefusal.Ok : ToolRefusal.NoCoins;
                }
                case ToolKind.Sickle:
                {
                    if (!_plots.TryGetValue(uid, out var plot)) return ToolRefusal.WrongTarget;
                    if (plot.State(now) != PlotState.Ripe || plot.Crop == null) return ToolRefusal.NotReady;
                    return FarmhouseRoom > 0 ? ToolRefusal.Ok : ToolRefusal.StorageFull;
                }
                case ToolKind.Hand:
                {
                    if (_animals.TryGetValue(uid, out var a))
                    {
                        if (!Working(uid)) return ToolRefusal.NotWorking;
                        if (a.StockAt(now) <= 0 && (a.Side == null || a.Side.StockAt(now) <= 0)) return ToolRefusal.NotReady;
                        return FarmhouseRoom > 0 ? ToolRefusal.Ok : ToolRefusal.StorageFull;
                    }
                    if (_machines.TryGetValue(uid, out var m))
                    {
                        if (!Working(uid)) return ToolRefusal.NotWorking;
                        m.Advance(now);
                        if (m.Output <= 0) return ToolRefusal.NotReady;
                        return FarmhouseRoom > 0 ? ToolRefusal.Ok : ToolRefusal.StorageFull;
                    }
                    return ToolRefusal.WrongTarget;
                }
                case ToolKind.Feed:
                {
                    if (!_animals.TryGetValue(uid, out var a) || !a.Producer.Eats) return ToolRefusal.WrongTarget;
                    var v = GameplayRules.FeedValue(tool.Item);
                    if (v <= 0) return ToolRefusal.WrongTarget;
                    if (!Working(uid)) return ToolRefusal.NotWorking;
                    if (a.Feed + v > GameplayRules.TroughSize) return ToolRefusal.Full;
                    return StoredInFarmhouses(tool.Item) > 0 ? ToolRefusal.Ok : ToolRefusal.NoStock;
                }
                case ToolKind.Input:
                {
                    if (!_machines.TryGetValue(uid, out var m)) return ToolRefusal.WrongTarget;
                    if (!m.Recipe.Inputs.Any(i => i.item == tool.Item)) return ToolRefusal.WrongTarget;
                    if (!Working(uid)) return ToolRefusal.NotWorking;
                    if (m.HopperOf(tool.Item) >= GameplayRules.HopperSize) return ToolRefusal.Full;
                    return StoredInFarmhouses(tool.Item) > 0 ? ToolRefusal.Ok : ToolRefusal.NoStock;
                }
                default:
                    return ToolRefusal.WrongTarget;
            }
        }

        /// <summary>
        /// Uses <paramref name="tool"/> on a building: plants, harvests the whole field, collects
        /// everything made, or fills the trough/hopper as far as the farmhouse stock and the room allow.
        /// </summary>
        public ToolUse Use(int uid, Tool tool, double now)
        {
            var check = CanUse(uid, tool, now);
            if (check != ToolRefusal.Ok) return new ToolUse(check, tool.Item);
            switch (tool.Kind)
            {
                case ToolKind.Seed:
                    return Plant(uid, tool.Item, now) == GatherResult.Done
                        ? new ToolUse(ToolRefusal.Ok, tool.Item, 1)
                        : new ToolUse(ToolRefusal.NotReady, tool.Item);

                case ToolKind.Sickle:
                {
                    var plot = _plots[uid];
                    var crop = plot.Crop!;
                    var stored = StoreInFarmhouses(crop, plot.Left);
                    if (stored <= 0) return new ToolUse(ToolRefusal.StorageFull, crop);
                    plot.Left -= stored;
                    Count("harvested", stored);
                    if (plot.Left <= 0) plot.Reset();
                    RefreshBasket();
                    return new ToolUse(ToolRefusal.Ok, crop, stored);
                }

                case ToolKind.Hand when _animals.TryGetValue(uid, out var a):
                {
                    var n = 0;
                    var item = a.Producer.Item;
                    while (a.StockAt(now) > 0 && StoreInFarmhouses(a.Producer.Item, 1) == 1)
                    {
                        a.Take(now);
                        Count("made:" + a.Producer.Item);
                        n++;
                    }
                    // the second good (the mine's ore) once the first is all in
                    while (a.StockAt(now) <= 0 && a.Side is { } side && side.StockAt(now) > 0 && StoreInFarmhouses(side.Producer.Item, 1) == 1)
                    {
                        side.Take(now);
                        Count("made:" + side.Producer.Item);
                        if (n == 0) item = side.Producer.Item;
                        n++;
                    }
                    RefreshBasket();
                    return n > 0 ? new ToolUse(ToolRefusal.Ok, item, n) : new ToolUse(ToolRefusal.StorageFull, item);
                }

                case ToolKind.Hand when _machines.TryGetValue(uid, out var m):
                {
                    m.Advance(now);
                    var stored = StoreInFarmhouses(m.Recipe.Output, m.Output);
                    if (stored <= 0) return new ToolUse(ToolRefusal.StorageFull, m.Recipe.Output);
                    m.Output -= stored;
                    Count("made:" + m.Recipe.Output, stored);
                    m.Advance(now);   // room again: the next batch can start
                    RefreshBasket();
                    return new ToolUse(ToolRefusal.Ok, m.Recipe.Output, stored);
                }

                case ToolKind.Feed:
                {
                    var a2 = _animals[uid];
                    var v = GameplayRules.FeedValue(tool.Item);
                    var units = Math.Min(StoredInFarmhouses(tool.Item), (GameplayRules.TroughSize - a2.Feed) / v);
                    if (units <= 0 || !TakeFromFarmhouses(tool.Item, units)) return new ToolUse(ToolRefusal.Full, tool.Item);
                    for (var i = 0; i < units; i++) a2.AddFeed(now, v);
                    Count("fed", units);
                    return new ToolUse(ToolRefusal.Ok, tool.Item, units);
                }

                case ToolKind.Input:
                {
                    var m2 = _machines[uid];
                    var units = Math.Min(StoredInFarmhouses(tool.Item), GameplayRules.HopperSize - m2.HopperOf(tool.Item));
                    if (units <= 0 || !TakeFromFarmhouses(tool.Item, units)) return new ToolUse(ToolRefusal.Full, tool.Item);
                    m2.Fill(tool.Item, units);
                    m2.Advance(now);
                    return new ToolUse(ToolRefusal.Ok, tool.Item, units);
                }

                default:
                    return new ToolUse(ToolRefusal.WrongTarget, tool.Item);
            }
        }
    }
}
