#nullable enable
using System;
using System.Globalization;
using Farmverse.Contracts;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Orders & Goals (design: missions.html, the pack's Missions screen): a list panel on the left and a reward
    // chest panel on the right. Opened as page "orders" with args "orders" (default) or "goals".
    // Orders: the island's real post office order (one at a time; the farmer carries the goods into the post
    // office). Goals: today's three daily goals and the story goals are the economy's own; achievements and
    // both chests are LOCAL PLACEHOLDERS kept on the device until the backend owns them.
    public sealed partial class GameUi
    {
        private static readonly bool RegShop_Orders = Register("orders", UiLayer.Page, (ui, c) => ui.ShopOrdersPage(c), .5f);

        private string _shopGoalTab = "daily";

        private void ShopOrdersPage(Ctx c)
        {
            if (c.Args as string == "goals") ShopGoals(c);
            else ShopOrders(c);
        }

        /// <summary>The shared frame: dotted screen, header (pills hidden on phone), body row with the list and the chest.</summary>
        private (VisualElement body, Label? coins, Label? gems) ShopFrame(Ctx c, string title, VisualElement? extra)
        {
            var p = c.P;
            var screen = PK.Screen(c.Root, true);
            var H = p.Phone ? 52f : 96f;
            var (coins, gems) = PK.Header(screen, p, title, Back, () => Go("home"), "0", "0", extra, !p.Phone,
                () => Open("shop", "coins"), () => Open("shop", "gems"));
            if (coins != null)
                c.Live(() =>
                {
                    coins.text = Loc.Of(PK.Num(Economy?.Coins ?? 0));
                    gems.text = Loc.Of(PK.Num(Wallet.Gems));
                });
            var pad = p.P(24);
            var body = PK.Row(screen);
            PK.Abs(body, left: p.L + pad, top: p.U(H) + p.P(10), right: p.R + pad, bottom: p.B + pad);
            body.style.alignItems = Align.Stretch;
            return (body, coins, gems);
        }

        // ------------------------------------------------------------------ orders

        private void ShopOrders(Ctx c)
        {
            var p = c.P;
            var eco = Economy;
            var orders = eco?.Orders.ToList() ?? new List<DeliveryOrder>();
            c.Watch(() => eco == null ? "none" : string.Join(";", eco.Orders.Select(o => o.Item + o.Given + "/" + ShopCarried(o.Item) + "/" + ShopHave(o.Item))) + "|" + eco.BestLevel);

            var timer = ShopNavyPill(p);
            PK.Pad(timer, p.U(p.Phone ? 10 : 18), 0, p.U(p.Phone ? 10 : 18), 0);
            PK.Img(timer, "Icon_PictoIcon_Timer", p.U(p.Phone ? 20 : 34)).style.marginRight = p.U(6);
            var timerText = PK.T(timer, p, "", 22);
            c.Live(() =>
            {
                var o = Economy?.Orders.OrderBy(x => x.Expires).FirstOrDefault();
                timerText.text = Loc.Of(o != null ? "Order ends in " + ShopSpan(o.Expires - IslandEconomyHost.Clock) : eco != null && eco.Has("post_office") ? "New order soon" : "No post office yet");
            });
            var (body, _, _) = ShopFrame(c, "Orders", timer);

            var left = PK.Panel(body, p);
            left.style.flexGrow = 1;
            left.style.minWidth = 0;
            left.style.flexDirection = FlexDirection.Column;
            PK.Pad(left, p.P(16), p.P(16), p.P(16), p.P(16));
            var head = PK.Row(left);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.flexShrink = 0;
            PK.Pad(head, p.P(6), 0, p.P(6), 0);
            head.style.marginBottom = p.P(12);
            PK.T(head, p, "TODAY'S ORDERS", 26);
            PK.N(head, p, "Carry the goods into the Post Office to deliver", 17, GameTheme.T("#9fd0ff"));
            var list = ShopScroller(left, false, p.P(12)).contentContainer;

            var rh = p.P(118);
            if (orders.Count == 0)
            {
                var none = PK.ListRow(list, p, false, true);
                none.style.height = rh;
                PK.Pad(none, p.P(16), 0, p.P(16), 0);
                ShopPortrait(none, p, "Icon_Mail", "#ffb03a");
                var postLevel = GameplayRules.UnlockLevel("post_office");
                PK.N(none, p, eco != null && eco.Has("post_office")
                    ? "The townsfolk are writing their next order…"
                    : eco != null && eco.BestLevel < postLevel
                        ? $"Delivery orders open at island level {postLevel}, with the post office."
                        : "Build a Post Office: the townsfolk send delivery orders there.", 20, GameTheme.T("#dff1ff"), TextAnchor.MiddleLeft, true).style.marginLeft = p.P(16);
            }
            foreach (var order in orders)
            {
                var carried = ShopCarried(order.Item);
                var have = order.Given + carried;
                var ready = carried >= order.Left;
                var row = PK.ListRow(list, p, false, false);
                row.style.height = rh;
                PK.Pad(row, p.P(16), 0, p.P(16), 0);
                ShopPortrait(row, p, "Icon_Mail", "#ffb03a");
                var who = PK.Col(row);
                who.style.width = p.P(150);
                who.style.flexShrink = 0;
                who.style.marginLeft = p.P(16);
                PK.T(who, p, "Post Office", 22);
                PK.N(who, p, "wants:", 15, GameTheme.T("#8ad6ff")).style.marginTop = p.P(8);
                var needs = PK.Row(row);
                needs.style.flexGrow = 1;
                needs.style.minWidth = 0;
                needs.style.overflow = Overflow.Hidden;
                needs.style.marginLeft = p.P(16);
                ShopNeed(needs, p, order.Item, Mathf.Min(have, order.Count), order.Count);
                var store = ShopHave(order.Item);
                if (store > 0) PK.N(needs, p, PK.Num(store) + " in store", 15, GameTheme.T("#8ad6ff")).style.marginLeft = p.P(10);
                var rw = PK.Col(row);
                rw.style.width = p.P(100);
                rw.style.flexShrink = 0;
                rw.style.marginLeft = p.P(16);
                var coinRow = PK.Row(rw);
                PK.Img(coinRow, "ResourceBar_Icon_Gold", p.P(36)).style.marginRight = p.P(6);
                PK.T(coinRow, p, PK.Num(order.Reward), 22);
                var btn = ready
                    ? PK.Btn(row, p, "green", "Deliver", 150, 60, 24, () => { Say("Walk into the Post Office with your basket"); Go("play"); })
                    : PK.Btn(row, p, "gray", "Need more", 150, 60, 19, () => Say(store > 0 ? "Take " + ShopGoodName(order.Item).ToLowerInvariant() + " from the farmhouse into your basket" : "Grow or make more " + ShopGoodName(order.Item).ToLowerInvariant()));
                btn.style.marginLeft = p.P(16);
            }

            // the chest: six orders today (contents: the catalogue; opened once a day by the server)
            var ordersNeeded = Mathf.Max(1, Wallet.Prices.OrderChestOrders);
            var done = Mathf.Min(ordersNeeded, Economy?.OrdersToday ?? 0);
            ShopChest(body, p, "ORDER CHEST", ChestId.Order, done, ordersNeeded);
        }

        /// <summary>How many of the good the farmer carries in the basket.</summary>
        private int ShopCarried(string item) => Economy?.Basket.CountOf(item) ?? 0;

        private static void ShopPortrait(VisualElement row, Pt p, string icon, string colour)
        {
            var por = PK.Centre(PK.Box(row));
            PK.Size(por, p.P(80), p.P(80));
            por.style.backgroundColor = GameTheme.C(colour);
            Kit.Round(por, p.P(80));
            Kit.Border(por, p.U(3), PK.Ink);
            PK.Img(por, icon, p.P(80) * .66f);
        }

        /// <summary>A need chip: the good, have/need (green when enough, red when short) and a tick.</summary>
        private static void ShopNeed(VisualElement parent, Pt p, string item, int have, int need)
        {
            var chip = PK.Row(parent);
            chip.style.flexShrink = 0;
            chip.style.backgroundColor = PK.Ink;
            Kit.Round(chip, p.U(10));
            PK.Pad(chip, p.P(5), p.P(5), p.P(8), p.P(5));
            chip.Add(ShopGoodIcon(p, item, p.P(40)));
            var ok = have >= need;
            var t = PK.T(chip, p, PK.Num(have) + "/" + PK.Num(need), 19, ok ? GameTheme.C("#7dff6a") : GameTheme.C("#ff7a8c"));
            t.style.unityTextOutlineWidth = p.P(3);
            t.style.marginLeft = p.P(5);
            if (ok) PK.Img(chip, "Toggle01_Check_Green", p.P(22)).style.marginLeft = p.P(5);
        }

        /// <summary>The right panel: title, glowing chest, progress, rewards, reset timer and Claim (claimed once a day on this device).</summary>
        /// <summary>What a chest holds, as reward circles (the catalogue's chests).</summary>
        private static (string icon, string value, string bg)[] ShopChestRewards(ChestId chest)
        {
            var list = new List<(string, string, string)>();
            if (!Wallet.Prices.Chests.TryGetValue(ChestIdWire.ToWire(chest), out var b)) return list.ToArray();
            if (b.Coins > 0) list.Add(("ResourceBar_Icon_Gold", PK.Num(b.Coins), "#ffd23a"));
            if (b.Gems > 0) list.Add(("ResourceBar_Icon_Gem", PK.Num(b.Gems), "#b05cff"));
            foreach (var kv in b.Boosts)
                list.Add((ShopBoosts.FirstOrDefault(x => x.id == kv.Key).art ?? "Icon_Booster", kv.Value.ToString(CultureInfo.InvariantCulture), "#3fc3ff"));
            return list.ToArray();
        }

        private bool _shopOpening;

        /// <summary>Opens today's chest (shop-claimChest): gems and boosts land on the server, coins on the island.</summary>
        private async void ShopOpenChest(ChestId chest)
        {
            var shop = Wallet.Shop;
            if (_shopOpening) return;
            if (shop == null || !shop.Online)
            {
                Say("Connect to the internet to open the chest");
                return;
            }
            var host = Host;
            if (host == null) return;
            _shopOpening = true;
            var result = await shop.ClaimChest(chest, JsonUtility.ToJson(host.CurrentState()));
            _shopOpening = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code switch
                {
                    ErrorCode.ChestClaimed => "Come back tomorrow for a new chest",
                    ErrorCode.ChestLocked => "Not finished yet",
                    _ => "Couldn't open it — check your connection and try again",
                });
                MarkDirty();
                return;
            }
            if (result.Value.Fresh)
            {
                if (result.Value.Coins > 0) Economy?.AddCoins((int)result.Value.Coins);
                IslandAudio.Play("cheer");
                Say("Chest opened!");
            }
            MarkDirty();
        }

        private void ShopChest(VisualElement body, Pt p, string title, ChestId chestId, int n, int of)
        {
            var rewards = ShopChestRewards(chestId);
            var key = chestId == ChestId.Order ? "orders" : "dailies";
            var side = PK.Panel(body, p);
            side.style.width = p.P(p.Phone ? 420 : 380);
            side.style.flexShrink = 0;
            side.style.marginLeft = p.P(20);
            side.style.flexDirection = FlexDirection.Column;
            side.style.alignItems = Align.Center;
            PK.Pad(side, p.P(20), p.P(20), p.P(20), p.P(20));
            var glow = PK.Abs(PK.Box(side), top: p.P(70), width: p.P(260), height: p.P(260));
            glow.style.left = new Length(50, LengthUnit.Percent);
            glow.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            Kit.Contain(glow, GameArt.Get("CardFrame06_Glow"));
            glow.style.opacity = GameTheme.Fade(.35f);
            var info = PK.Img(side, "Icon_ImageIcon_Info", p.P(34));
            PK.Abs(info, right: p.P(12), top: p.P(12));
            PK.Tap(info, p, () => Say(key == "orders" ? "Fill " + of + " orders in a day to open the chest." : "Finish all of today's tasks to open the chest."));
            PK.T(side, p, title, 24, null, true, TextAnchor.MiddleCenter);
            var chest = PK.Img(side, "ShopItem_SpecialChest_Purple", p.P(p.Phone ? 170 : 190));
            chest.style.marginTop = p.P(14);
            var (bar, _, _) = PK.Bar(side, p, 0, 34, of > 0 ? n * 100f / of : 0, n + " / " + of);
            bar.style.width = new Length(90, LengthUnit.Percent);
            bar.style.marginTop = p.P(14);
            var rw = PK.Row(side);
            rw.style.justifyContent = Justify.Center;
            rw.style.flexWrap = Wrap.Wrap;
            rw.style.marginTop = p.P(14);
            var s = p.P(78);
            for (var i = 0; i < rewards.Length; i++)
            {
                var col = PK.Col(rw);
                col.style.alignItems = Align.Center;
                if (i > 0) col.style.marginLeft = p.P(12);
                var circle = PK.Centre(PK.Box(col));
                PK.Size(circle, s, s);
                circle.style.backgroundColor = GameTheme.C(rewards[i].bg);
                Kit.Round(circle, s);
                Kit.Border(circle, p.U(3), PK.Ink);
                PK.Img(circle, rewards[i].icon, s * .62f);
                PK.T(col, p, rewards[i].value, 20).style.marginTop = p.P(4);
            }

            PK.Grow(side);
            var t = PK.Row(side);
            PK.Img(t, "Icon_PictoIcon_Timer", p.P(26)).style.marginRight = p.P(6);
            var tl = PK.N(t, p, "", 16, GameTheme.T("#9fd0ff"));
            tl.enableRichText = true;
            var claimed = Wallet.Shop?.ChestClaimed(chestId, ShopUtcDay) ?? false;
            var ready = n >= of && !claimed;
            tl.text = Loc.Of("Resets in <b><color=#ff7a8c>" + ShopUntilMidnight() + "</color></b>");
            var btn = PK.Btn(side, p, ready ? "yellow" : "gray", claimed ? "Claimed" : "Claim", 260, 72, 30, () =>
            {
                if (!ready)
                {
                    Say(claimed ? "Come back tomorrow for a new chest" : "Not finished yet");
                    return;
                }

                ShopOpenChest(chestId);
            });
            btn.style.marginTop = p.P(14);
        }

        // ------------------------------------------------------------------ goals

        private bool _shopClaimingAch;

        /// <summary>Collects an achievement (goals-claimAchievement): the server checks the stat on its island copy.</summary>
        private async void ShopClaimAchievement(string id, int gems)
        {
            var shop = Wallet.Shop;
            var host = Host;
            if (_shopClaimingAch || host == null) return;
            if (shop == null || !shop.Online)
            {
                Say("Connect to the internet to collect it");
                return;
            }
            _shopClaimingAch = true;
            var result = await shop.ClaimAchievement(id, JsonUtility.ToJson(host.CurrentState()));
            _shopClaimingAch = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code switch
                {
                    ErrorCode.AchievementClaimed => "Already collected",
                    ErrorCode.AchievementLocked => "Not reached yet",
                    _ => "Couldn't collect it — check your connection and try again",
                });
                MarkDirty();
                return;
            }
            if (result.Value.Fresh)
            {
                IslandAudio.Play("cheer");
                Say("+" + result.Value.Granted + " gems");
            }
            MarkDirty();
        }

        private sealed class ShopGoalRow
        {
            public string text = "", icon = "Icon_Star";
            public string? sub, coupon;
            public int have, need, coins, gems;
            public bool claimed, go;
            public Action? claim;
        }

        private void ShopGoals(Ctx c)
        {
            var p = c.P;
            var eco = Economy;
            c.Watch(() => eco == null ? "" : string.Join(",", eco.Dailies.Select(i => eco.DailyHave(i) + (eco.DailyClaimed(i) ? "c" : ""))) + "|" + eco.GoalIndex + ":" + (eco.CurrentGoal != null ? eco.GoalHave(eco.CurrentGoal) : 0));
            var (body, _, _) = ShopFrame(c, "Goals", null);

            var left = PK.Panel(body, p);
            left.style.flexGrow = 1;
            left.style.minWidth = 0;
            left.style.flexDirection = FlexDirection.Column;
            PK.Pad(left, p.P(14), p.P(14), p.P(14), p.P(14));
            PK.Seg(left, p, new[] { ("daily", "Daily tasks"), ("story", "Story goals"), ("ach", "Achievements") }, _shopGoalTab, id =>
            {
                _shopGoalTab = id;
                MarkDirty();
            });
            var listSv = ShopScroller(left, false, p.P(12));
            listSv.style.marginTop = p.P(12);
            var list = listSv.contentContainer;

            var rows = new List<ShopGoalRow>();
            if (eco != null)
            {
                if (_shopGoalTab == "daily")
                    foreach (var i in eco.Dailies)
                    {
                        var d = GameplayRules.DailyPool[i];
                        var pool = i;
                        rows.Add(new ShopGoalRow
                        {
                            text = eco.DailyText(i), icon = ShopStatArt(d.Stat), have = eco.DailyHave(i), need = eco.DailyNeed(i), coins = eco.DailyCoins(i), claimed = eco.DailyClaimed(i),
                            claim = () =>
                            {
                                if (!eco.ClaimDaily(pool)) return;
                                IslandAudio.Play("cheer");
                                Say("+" + PK.Num(eco.DailyCoins(pool)) + " coins");
                            },
                        });
                    }
                else if (_shopGoalTab == "story")
                {
                    // the story never ends: written goals, then a level every 5
                    for (var g = Mathf.Max(0, eco.GoalIndex - 1); g < eco.GoalIndex + 4; g++)
                    {
                        var goal = GameplayRules.GoalAt(g);
                        var current = g == eco.GoalIndex;
                        rows.Add(new ShopGoalRow
                        {
                            text = goal.Text, sub = goal.Hint, icon = ShopStatArt(goal.Stat), need = goal.Need, coins = goal.Reward,
                            have = g < eco.GoalIndex ? goal.Need : current ? eco.GoalHave(goal) : 0,
                            claimed = g < eco.GoalIndex, go = current,
                            claim = current ? () =>
                            {
                                if (!eco.ClaimGoal()) return;
                                IslandAudio.Play("cheer");
                                Say("+" + PK.Num(goal.Reward) + " coins");
                            } : (Action?)null,
                        });
                    }

                    if (eco.CurrentGoal == null)
                        rows.Add(new ShopGoalRow { text = "Every story goal done!", icon = "Icon_ImageIcon_Medal_Gold", have = 1, need = 1, claimed = true, sub = "Keep growing: daily tasks still pay." });
                }
                else
                    // the catalogue's achievements; collected ones come from the server (state/goals)
                    foreach (var kv in Wallet.Prices.Achievements.OrderBy(kv => kv.Value.Order).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        var (id, a) = (kv.Key, kv.Value);
                        rows.Add(new ShopGoalRow
                        {
                            text = a.Title, sub = a.About, icon = a.Icon, have = eco.Stat(a.Stat), need = a.Need, gems = a.Gems,
                            claimed = Wallet.Shop?.AchievementClaimed(id) ?? false,
                            claim = () => ShopClaimAchievement(id, a.Gems),
                        });
                    }
            }

            var rh = p.P(112);
            for (var k = 0; k < rows.Count; k++)
            {
                var g = rows[k];
                var ready = g.have >= g.need && !g.claimed;
                var row = PK.ListRow(list, p, ready, g.claimed);
                row.style.minHeight = rh;   // grows when a goal has a line under its name
                row.style.flexShrink = 0;
                if (k > 0) row.style.marginTop = p.P(14);
                PK.Pad(row, p.P(16), p.P(12), p.P(16), p.P(12));
                var art = PK.Fit(row, g.icon, p.P(84), p.P(78));
                art.style.flexShrink = 0;
                var txt = PK.Col(row);
                txt.style.flexGrow = 1;
                txt.style.minWidth = 0;
                txt.style.justifyContent = Justify.Center;
                txt.style.marginLeft = p.P(16);
                PK.T(txt, p, g.text, 26);
                if (g.sub != null) PK.N(txt, p, g.sub, 15, GameTheme.T("#bfe6ff"), TextAnchor.MiddleLeft, true).style.marginTop = p.P(6);
                // the bar sits under the goal's name, as wide as the row allows (the name is never covered)
                var shown = Mathf.Min(g.have, g.need);
                var (bar, _, _) = PK.Bar(txt, p, p.Phone ? 220 : 260, 32, g.need > 0 ? shown * 100f / g.need : 0, PK.Num(shown) + " / " + PK.Num(g.need));
                bar.style.width = new Length(100, LengthUnit.Percent);
                bar.style.maxWidth = p.P(560);
                bar.style.marginTop = p.P(8);
                var rwBox = PK.Centre(PK.Box(row));
                rwBox.style.width = p.P(150);
                rwBox.style.flexShrink = 0;
                rwBox.style.marginLeft = p.P(16);
                ShopRewardChip(rwBox, p, g);
                VisualElement btn;
                var row1 = g;
                if (g.claimed) btn = PK.Btn(row, p, "gray", "✓", 150, 64, 26);
                else if (ready && g.claim != null)
                    btn = PK.Btn(row, p, "green", "Claim", 150, 64, 26, () =>
                    {
                        row1.claim!();
                        MarkDirty();
                    });
                else if (g.go)
                    btn = PK.Btn(row, p, "blue", "Go", 150, 64, 26, () =>
                    {
                        Say(row1.sub ?? row1.text, 3f);
                        Go("play");
                    });
                else continue;   // nothing to press yet: the reward stays at the row's right edge

                btn.style.flexShrink = 0;
                btn.style.marginLeft = p.P(16);
            }

            // the chest: all of today's tasks done
            var claimedToday = eco != null ? eco.Dailies.Count(eco.DailyClaimed) : 0;
            var total = eco != null ? eco.Dailies.Count : 3;
            ShopChest(body, p, "DAILY TASK CHEST", ChestId.Task, claimedToday, Mathf.Max(1, total));
        }

        /// <summary>The reward column: coins or gems with the amount (coupon chip when a goal pays a coupon).</summary>
        private static void ShopRewardChip(VisualElement parent, Pt p, ShopGoalRow g)
        {
            if (g.coupon != null)
            {
                var cpn = PK.Row(parent);
                cpn.style.backgroundColor = PK.Ink;
                Kit.Round(cpn, p.P(100));
                PK.Pad(cpn, p.P(6), p.P(6), p.P(12), p.P(6));
                cpn.Add(ShopCouponIcon(p.P(52)));
                PK.T(cpn, p, g.coupon, 18).style.marginLeft = p.U(4);
                return;
            }

            if (g.coins <= 0 && g.gems <= 0) return;
            var col = PK.Col(parent);
            col.style.alignItems = Align.Center;
            col.style.width = p.P(90);
            PK.Img(col, g.gems > 0 ? "ResourceBar_Icon_Gem" : "ResourceBar_Icon_Gold", p.P(46));
            PK.T(col, p, PK.Num(g.gems > 0 ? g.gems : g.coins), 22);
        }

        /// <summary>The picture for a goal by the stat it counts.</summary>
        private static string ShopStatArt(string stat) => stat switch
        {
            "harvested" => "Icon_Pumkin",
            "planted" => "Icon_Shovel",
            "sold" => "Icon_Golds",
            "chopped" => "G_Tree",
            "stored" => "Icon_Chest",
            "orders" => "Icon_ImageIcon_Map_l",
            "fed" => "G_Wheat",
            "raids_won" => "Icon_Shield",
            "level" => "Icon_Star",
            _ when stat.StartsWith("made:", StringComparison.Ordinal) => ShopGoodArt(stat.Substring(5)) ?? "Icon_GearWheels",
            _ when stat.StartsWith("has:", StringComparison.Ordinal) => "Icon_Hammer",
            _ => "Icon_Star",
        };
    }
}
