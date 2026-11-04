#nullable enable
using System.Linq;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The freight train's page (from island level 5): three crates, each loaded from the stores, paid for
    /// in building materials and coins when the train leaves. A full train pays an extra crate of materials.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegTrain = Register("train", UiLayer.Page, (ui, c) => ui.TrainPage(c), .5f);

        private void TrainPage(Ctx c)
        {
            var p = c.P;
            var eco = Economy;
            c.Watch(() => eco == null ? "" : (eco.Train == null ? "away" : string.Join(",", eco.Train.Crates.Select(k => k.Loaded + ":" + eco.InStore(k.Item)))) + "|" + eco.BestLevel);

            var timer = ShopNavyPill(p);
            PK.Pad(timer, p.U(p.Phone ? 10 : 18), 0, p.U(p.Phone ? 10 : 18), 0);
            PK.Img(timer, "Icon_PictoIcon_Timer", p.U(p.Phone ? 20 : 34)).style.marginRight = p.U(6);
            var timerText = PK.T(timer, p, "", 22);
            c.Live(() =>
            {
                var e = Economy;
                if (e == null) return;
                var now = IslandEconomyHost.Clock;
                timerText.text = Loc.Of(e.BestLevel < GameplayRules.TrainLevel ? $"Opens at level {GameplayRules.TrainLevel}"
                    : e.Train != null ? "Leaves in " + ShopSpan(e.Train.LeavesAt - now)
                    : e.NextTrainAt > now ? "Next truck in " + ShopSpan(e.NextTrainAt - now) : "Arriving…");
            });
            var (body, _, _) = ShopFrame(c, "Order Truck", timer);

            var panel = PK.Panel(body, p);
            panel.style.flexGrow = 1;
            panel.style.minWidth = 0;
            panel.style.flexDirection = FlexDirection.Column;
            PK.Pad(panel, p.P(16), p.P(16), p.P(16), p.P(16));
            var head = PK.Row(panel);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.marginBottom = p.P(12);
            PK.T(head, p, "CRATES", 26);
            PK.N(head, p, "Load crates from your stores: the truck pays in building materials", 17, GameTheme.T("#9fd0ff"));
            var list = ShopScroller(panel, false, p.P(12)).contentContainer;

            if (eco == null || eco.BestLevel < GameplayRules.TrainLevel || eco.Train == null)
            {
                var none = PK.ListRow(list, p, false, true);
                none.style.height = p.P(118);
                PK.Pad(none, p.P(16), 0, p.P(16), 0);
                ShopPortrait(none, p, "Icon_Chest", "#ffb03a");
                PK.N(none, p, eco == null || eco.BestLevel < GameplayRules.TrainLevel
                        ? $"The order truck starts calling at your depot at island level {GameplayRules.TrainLevel}."
                        : "The truck is out on the road. It'll be back with new crates soon.",
                    20, GameTheme.T("#dff1ff"), TextAnchor.MiddleLeft, true).style.marginLeft = p.P(16);
                return;
            }

            var train = eco.Train;
            for (var i = 0; i < train.Crates.Count; i++)
            {
                var crate = train.Crates[i];
                var index = i;
                var have = eco.InStore(crate.Item);
                var row = PK.ListRow(list, p, false, crate.Loaded);
                row.style.height = p.P(118);
                PK.Pad(row, p.P(16), 0, p.P(16), 0);
                ShopPortrait(row, p, "Icon_Chest", crate.Loaded ? "#5fd35a" : "#ffb03a");
                var needs = PK.Row(row);
                needs.style.flexGrow = 1;
                needs.style.minWidth = 0;
                needs.style.marginLeft = p.P(16);
                ShopNeed(needs, p, crate.Item, crate.Loaded ? crate.Need : Mathf.Min(have, crate.Need), crate.Need);
                if (!crate.Loaded) PK.N(needs, p, PK.Num(have) + " in store", 15, GameTheme.T("#8ad6ff")).style.marginLeft = p.P(10);
                // what it brings back
                var pays = PK.Col(row, p.P(2));
                pays.style.width = p.P(190);
                pays.style.flexShrink = 0;
                PK.N(pays, p, string.Join(", ", crate.Pays.Select(x => x.count + " " + Loc.Of(ShopGoodName(x.item)))), 16, GameTheme.T("#dff1ff"));
                var coinRow = PK.Row(pays);
                PK.Img(coinRow, "ResourceBar_Icon_Gold", p.P(26)).style.marginRight = p.P(4);
                PK.T(coinRow, p, PK.Num(crate.Coins), 18);
                var btn = crate.Loaded
                    ? PK.Btn(row, p, "gray", "Loaded", 150, 60, 20, null)
                    : have >= crate.Need
                        ? PK.Btn(row, p, "green", "Load", 150, 60, 24, () =>
                        {
                            if (Economy?.LoadCrate(index) != true) return;
                            IslandAudio.Play("pop");
                            MarkDirty();
                        })
                        : PK.Btn(row, p, "gray", "Need more", 150, 60, 19, () => Say("Grow or make more " + ShopGoodName(crate.Item).ToLowerInvariant() + " and store it in the farmhouse"));
                btn.style.marginLeft = p.P(16);
            }

            var foot = PK.Row(panel, p.P(16));
            foot.style.justifyContent = Justify.SpaceBetween;
            foot.style.marginTop = p.P(12);
            PK.N(foot, p, train.AllLoaded ? "Full truck: one more crate of materials!" : "Fill all three for a bonus crate of materials", 17, GameTheme.T("#9fd0ff"));
            var anyLoaded = train.Crates.Any(k => k.Loaded);
            PK.Btn(foot, p, anyLoaded ? "yellow" : "gray", "Send truck", 210, 64, 24, () =>
            {
                if (Economy?.SendTrain(IslandEconomyHost.Clock) != true)
                {
                    Say("Load at least one crate first");
                    return;
                }
                IslandAudio.Play("cheer");
                MarkDirty();
            });
        }

        /// <summary>Every frame: say what a departing train paid, once.</summary>
        private void TrainTick()
        {
            if (Economy?.TakeTrainPayout() is not { } pay || pay.Crates == 0) return;
            // translated piece by piece: the materials vary
            var mats = string.Join(", ", pay.Materials.Select(kv => kv.Value + " " + Loc.Of(ShopGoodName(kv.Key))));
            Say(Loc.Of($"The truck paid {PK.Num(pay.Coins)} coins") + ": " + mats, 3.5f);
        }
    }
}
