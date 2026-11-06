#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Farmverse.Domain.Gameplay
{
    /// <summary>One change to an island's contents for the chain (docs: 13 §7).</summary>
    [Serializable]
    public sealed class ExchangeOp
    {
        /// <summary>
        /// <c>take</c> goods, <c>give</c> goods, <c>pickUp</c> a placed building, <c>hold</c> an unplaced one,
        /// <c>fee</c>: spend <see cref="bps"/> of the value the earlier ops moved, at least <see cref="n"/> coins,
        /// <c>credit</c>: add <see cref="n"/> coins (a fee given back), <c>openPlot</c>: clear the next wild plot
        /// (a land deed), <c>crew</c>: set the Farmhands from the chain.
        /// </summary>
        public string op = "";
        public string item = "";
        public int n;
        /// <summary>The placed building to pick up.</summary>
        public int uid;
        /// <summary>For <c>hold</c>: the building's catalogue id, level and price.</summary>
        public string id = "";
        public int level = 1;
        public int price;
        /// <summary>For <c>fee</c>: basis points of the value moved so far.</summary>
        public int bps;
        /// <summary>For <c>crew</c>: the Farmhands the player holds now (replaces the island's crew).</summary>
        public List<Farmhand>? crew;
    }

    /// <summary>What an op did: the goods moved with their market value, or the building picked up.</summary>
    [Serializable]
    public sealed class ExchangeDone
    {
        public string op = "";
        public string item = "";
        public int n;
        /// <summary>Market price of one unit of <see cref="item"/> (for mint caps).</summary>
        public int unitValue;
        public int uid;
        public string id = "";
        public int level = 1;
        public int price;
    }

    /// <summary>The island can't do what was asked (not enough goods, building busy …). Nothing changed.</summary>
    public sealed class ExchangeRefusal : Exception
    {
        public ExchangeRefusal(string code, string message) : base(message) { Code = code; }

        /// <summary><c>not_enough</c>, <c>no_coins</c>, <c>no_building</c>, <c>not_empty</c>, <c>busy</c>, <c>core_building</c>, <c>no_farmhouse</c>, <c>bad_op</c> …</summary>
        public string Code { get; }
    }

    /// <summary>
    /// Goods and buildings leaving the island for the chain (mint) and coming back (redeem), as one
    /// all-or-nothing step: bring the island to <c>now</c>, apply every op, return the new state.
    /// Deterministic and pure, like <see cref="IslandSim.Advance"/>.
    /// </summary>
    public static class IslandExchange
    {
        public const int MaxOps = 16;

        public static (IslandAdvance advance, List<ExchangeDone> done) Apply(IslandState state, double now, IReadOnlyList<ExchangeOp> ops)
        {
            if (ops.Count == 0 || ops.Count > MaxOps) throw new ExchangeRefusal("bad_op", $"between 1 and {MaxOps} ops");
            var advanced = IslandSim.Advance(state, now);
            var e = advanced.Economy;
            var buildings = advanced.State.buildings.Select(IslandSim.Copy).ToList();
            var held = advanced.State.held.Select(IslandSim.CopyHeld).ToList();
            var done = new List<ExchangeDone>();

            foreach (var op in ops)
            {
                switch (op.op)
                {
                    case "take":
                        CheckGoods(op);
                        if (!e.TakeFromFarmhouses(op.item, op.n))
                            throw new ExchangeRefusal("not_enough", $"only {e.StoredInFarmhouses(op.item)} {op.item} in store");
                        done.Add(new ExchangeDone { op = "take", item = op.item, n = op.n, unitValue = GameplayRules.PriceOf(op.item) });
                        break;
                    case "give":
                        CheckGoods(op);
                        if (!e.GiveToFarmhouses(op.item, op.n)) throw new ExchangeRefusal("no_farmhouse", "no farmhouse to store goods in");
                        done.Add(new ExchangeDone { op = "give", item = op.item, n = op.n, unitValue = GameplayRules.PriceOf(op.item) });
                        break;
                    case "pickUp":
                    {
                        var b = buildings.FirstOrDefault(x => x.uid == op.uid) ?? throw new ExchangeRefusal("no_building", $"no building {op.uid}");
                        var why = e.WhyNotPickUp(op.uid, now);
                        if (why != null) throw new ExchangeRefusal(why, $"building {op.uid} can't be picked up: {why}");
                        var level = e.LevelOf(op.uid);
                        e.RemoveBuilding(op.uid);
                        e.ForgetLevel(op.uid);
                        buildings.Remove(b);
                        done.Add(new ExchangeDone { op = "pickUp", uid = op.uid, id = b.id, level = level, price = b.price, item = b.id, n = 1, unitValue = b.price });
                        break;
                    }
                    case "hold":
                        if (string.IsNullOrEmpty(op.id) || op.level < 1 || op.price < 0) throw new ExchangeRefusal("bad_op", "hold needs id, level ≥ 1, price ≥ 0");
                        held.Add(new IslandState.Held { id = op.id, level = op.level, price = op.price });
                        done.Add(new ExchangeDone { op = "hold", id = op.id, level = op.level, price = op.price, item = op.id, n = 1, unitValue = op.price });
                        break;
                    case "fee":
                    {
                        if (op.n < 0 || op.bps < 0 || op.bps > 10_000) throw new ExchangeRefusal("bad_op", "fee needs n ≥ 0 and bps 0..10000");
                        long moved = done.Sum(d => (long)d.unitValue * d.n);
                        var fee = (int)Math.Max(op.n, moved * op.bps / 10_000);
                        if (!e.SpendCoins(fee)) throw new ExchangeRefusal("no_coins", $"only {e.Coins} coins for a fee of {fee}");
                        done.Add(new ExchangeDone { op = "fee", n = fee });
                        break;
                    }
                    case "openPlot":
                        if (!e.OpenWildPlot()) throw new ExchangeRefusal("all_open", "every wild plot is open already");
                        done.Add(new ExchangeDone { op = "openPlot", n = e.WildPlotsOpen });
                        break;
                    case "crew":
                        e.SetCrew(op.crew ?? new List<Farmhand>());
                        done.Add(new ExchangeDone { op = "crew", n = e.Crew.Count });
                        break;
                    case "credit":
                        if (op.n < 1 || op.n > 1_000_000_000) throw new ExchangeRefusal("bad_op", "n must be 1..1e9");
                        e.AddCoins(op.n);
                        done.Add(new ExchangeDone { op = "credit", n = op.n });
                        break;
                    default:
                        throw new ExchangeRefusal("bad_op", $"unknown op '{op.op}'");
                }
            }

            var next = new IslandState
            {
                rulesVersion = IslandSim.RulesVersion,
                simAt = now,
                seed = advanced.State.seed,
                startCoins = advanced.State.startCoins,
                buildings = buildings,
                held = held,
                economy = e.ToSave(),
            };
            return (new IslandAdvance(next, advanced.Report, IslandSim.NextWake(e, now), e), done);
        }

        private static void CheckGoods(ExchangeOp op)
        {
            if (string.IsNullOrEmpty(op.item) || op.item.Length > 40) throw new ExchangeRefusal("bad_op", "item missing");
            if (op.n < 1 || op.n > 1_000_000_000) throw new ExchangeRefusal("bad_op", "n must be 1..1e9");
        }
    }
}
