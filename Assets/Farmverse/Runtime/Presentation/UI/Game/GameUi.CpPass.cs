#nullable enable
using System;
using System.Linq;
using Farmverse.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The Farm Pass as the server runs it: seasons and tier prizes from the catalogue (<c>pass</c>), the player's
    /// season from <c>state/pass</c> (joined at an island XP; tiers collected; premium bought). Pass XP is island XP
    /// earned since joining, so the screen can show it live; the server checks it again when a tier is claimed.
    /// </summary>
    public static class CpPass
    {
        private static PassDef Def => Wallet.Prices.Pass;

        public static int Tiers => Def.Tiers.Count;
        public static int XpPerTier => Def.XpPerTier;
        public static int SeasonDays => Def.SeasonDays;

        private static PassDoc? Doc => Wallet.Shop?.Pass;

        /// <summary>Bought the premium pass this season.</summary>
        public static bool Active => Doc?.PremiumActive ?? false;

        private static double NowMs => Farmverse.Presentation.World.Island.IslandEconomyHost.Clock * 1000.0;

        private static long SeasonLenMs => SeasonDays * 86_400_000L;

        /// <summary>This season's number (1-based, for the screen).</summary>
        public static int SeasonNumber => 1 + (int)Math.Max(0, Math.Floor((NowMs - Def.EpochMs) / SeasonLenMs));

        public static TimeSpan Left(long xpNow)
        {
            var start = Def.EpochMs + (SeasonNumber - 1) * SeasonLenMs;
            return TimeSpan.FromMilliseconds(Math.Max(0, start + SeasonLenMs - NowMs));
        }

        public static int PassXp(long xpNow)
        {
            var doc = Doc;
            if (doc == null) return 0;   // not joined yet (offline first launch)
            return (int)Math.Max(0, (xpNow - doc.BaseXp) / Math.Max(1, Def.IslandXpPerPassXp));
        }

        /// <summary>The highest tier reached (1..Tiers).</summary>
        public static int Tier(long xpNow) => Mathf.Clamp(1 + PassXp(xpNow) / Math.Max(1, XpPerTier), 1, Math.Max(1, Tiers));

        public static int TierXp(long xpNow) => Tier(xpNow) >= Tiers ? XpPerTier : PassXp(xpNow) % Math.Max(1, XpPerTier);

        public static bool Claimed(int tier, bool premium)
        {
            var doc = Doc;
            return doc != null && (premium ? doc.Premium : doc.Free).Contains(tier);
        }

        public static bool CanClaim(int tier, bool premium, long xpNow) => Doc != null && tier <= Tier(xpNow) && !Claimed(tier, premium) && (!premium || Active);

        public static Bundle Prize(int tier, bool premium) => premium ? Def.Tiers[tier - 1].Premium : Def.Tiers[tier - 1].Free;

        public static (string art, string text) Reward(int tier, bool premium) => CpPrize.Tile(Prize(tier, premium));
    }

    // Farm Pass (pass.html): the pack's Pass screen and Popup_Pass. Phone k 0.5.
    public sealed partial class GameUi
    {
        private static readonly bool RegCpPass = Register("pass", UiLayer.Page, (ui, c) => ui.CpPassPage(c), .5f);
        private static readonly bool RegCpPassBuy = Register("passbuy", UiLayer.Popup, (ui, c) => ui.CpPassBuy(c), .5f);

        private long CpXp => Economy?.Xp ?? 0;

        private void CpPassPage(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var xp = CpXp;
            var cur = CpPass.Tier(xp);
            var active = CpPass.Active;
            c.Watch(() => CpPass.Tier(CpXp));
            c.Watch(() => CpPass.TierXp(CpXp));

            // purple strip on the left, the blue screen on the right (linear-gradient 90deg with a hard stop at 18%)
            var bg = PK.Inset(PK.Box(c.Root));
            bg.pickingMode = PickingMode.Position;
            PK.Grad(bg, "cp_passbg", false, (0f, GameTheme.C("#4b1d9c")), (.18f, GameTheme.C("#5a2fb8")), (.181f, GameTheme.T("#0d5aa4")), (1f, GameTheme.T("#0a4888")));

            var (coins, gems) = PK.Header(c.Root, p, "Farm Pass: " + Farmverse.Domain.Gameplay.GameplayRules.ThemeOf(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Theme, Back, () => Go("home"), PK.Num(Economy?.Coins ?? 0), PK.Num(Wallet.Gems), pills: !p.Phone, addCoins: () => Open("shop", "coins"), addGems: () => Open("shop", "gems"));
            if (coins != null) c.Live(() => coins.text = Loc.Of(PK.Num(Economy?.Coins ?? 0)));
            if (gems != null) c.Live(() => gems.text = Loc.Of(PK.Num(Wallet.Gems)));
            var H = p.U(p.Phone ? 52 : 96);
            var pad = P(24);

            // top row: season timer · level badge + XP bar · hint · Activate
            var top = PK.Row(c.Root, P(18));
            PK.Abs(top, left: p.L + pad, right: p.R + pad, top: H + P(8), height: P(80));
            var timer = PK.Row(top, P(10));
            timer.style.height = P(56);
            PK.Pad(timer, P(18), 0, P(18), 0);
            timer.style.backgroundColor = GameTheme.T("#041634", .8f);
            Kit.Round(timer, p.U(10));
            PK.Img(timer, "Icon_PictoIcon_Timer", P(36));
            var tc = PK.Col(timer, P(3));
            PK.N(tc, p, "Season ends in", 14, GameTheme.T("#9fd0ff"));
            var left = PK.T(tc, p, "", 22);
            c.Live(() =>
            {
                var l = CpPass.Left(CpXp);
                left.text = Loc.Of(l.TotalDays >= 1 ? $"{(int)l.TotalDays}d {l.Hours}h" : $"{l.Hours}h {l.Minutes}m");
            });

            var lv = PK.Row(top);
            var badge = PK.Centre(PK.Box(lv));
            PK.Size(badge, P(64), P(70));
            Kit.Contain(badge, GameArt.Get("Slider_Level01_Badge_Yellow"));
            PK.T(badge, p, cur.ToString(), 30);
            var tierXp = CpPass.TierXp(xp);
            var (bar, _, _) = PK.Bar(lv, p, 260, 36, tierXp * 100f / CpPass.XpPerTier, $"{tierXp} / {CpPass.XpPerTier} XP");
            bar.style.marginLeft = P(64 - 8);   // the badge sits over the bar's left end
            badge.style.position = Position.Absolute;
            badge.style.left = 0;
            badge.style.top = 0;
            badge.BringToFront();
            lv.style.height = P(70);
            PK.N(top, p, "Earn pass XP from orders and daily tasks.", 16, GameTheme.T("#bfe6ff"));
            PK.Grow(top);
            if (active)
            {
                var on = PK.Row(top, p.U(8));
                on.style.height = P(56);
                PK.Pad(on, P(20), 0, P(20), 0);
                on.style.backgroundColor = GameTheme.T("#041634", .8f);
                Kit.Round(on, p.U(10));
                PK.Img(on, "Toggle01_Check_Green", P(34));
                PK.T(on, p, "Farm Pass active", 22);
            }
            else
            {
                // the premium track: Get Premium (FARM) or use a Farm Pass NFT, in the popup
                var act = PK.Btn(top, p, ChainReady ? "purple" : "gray", null, 250, 72, 30, () => Open("passbuy"));
                var lockIcon = PK.Img(act, "Icon_ImageIcon_Lock01_s", P(44));
                lockIcon.style.marginRight = P(8);
                PK.T(act, p, "Premium", 30);
            }

            // the track: FARM PASS / FREE PASS labels, then the scrolling tier columns
            var tw = P(p.Phone ? 150 : 180);
            var th = P(p.Phone ? 176 : 220);
            var colW = P(p.Phone ? 176 : 212);
            var colH = th * 2 + P(16) * 2 + P(56);
            var area = PK.Row(c.Root, P(20));
            PK.Abs(area, left: p.L + pad, right: p.R, top: H + P(110), bottom: p.B + pad);

            var sides = PK.Col(area, P(16) + P(56));
            PK.Size(sides, P(200), float.NaN);
            foreach (var (gold, label) in new[] { (true, "FARM\nPASS"), (false, "FREE\nPASS") })
            {
                var s = PK.Centre(CpGradBox(sides, gold ? "sidegold" : "sideblue", GameTheme.C(gold ? "#ffd84a" : "#3fb2ff"), GameTheme.C(gold ? "#f2a600" : "#1676d1"), p.U(4), p.U(14), p.U(6)));
                s.style.height = th;
                var t = PK.T(s, p, label, 34, null, true, TextAnchor.MiddleCenter);
                t.style.whiteSpace = WhiteSpace.Normal;
            }

            var track = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            track.mode = ScrollViewMode.Horizontal;
            track.style.flexGrow = 1;
            track.style.height = colH + P(50);
            area.Add(track);
            var cc = track.contentContainer;
            PK.Pad(cc, P(10), P(40), P(30), P(10));
            var row = PK.Row(cc);
            row.style.alignItems = Align.FlexStart;
            var lineY = th + P(16) + P(28) - P(9);
            var line = PK.Abs(PK.Box(row), left: 0, top: lineY, width: CpPass.Tiers * colW, height: P(18));
            line.style.backgroundColor = PK.Ink;
            Kit.Round(line, p.U(9));
            var gold2 = PK.Abs(PK.Box(row), left: 0, top: lineY, width: (cur - .5f) * colW, height: P(18));
            PK.Grad(gold2, "cp_passline", true, (0f, GameTheme.C("#ffe36a")), (1f, GameTheme.C("#f2a600")));
            Kit.Round(gold2, p.U(9));

            for (var t = 1; t <= CpPass.Tiers; t++)
            {
                var col = PK.Col(row, P(16));
                col.style.alignItems = Align.Center;
                PK.Size(col, colW, colH);
                CpPassTile(col, c, t, true, tw, th);
                var node = PK.Centre(PK.Box(col));
                PK.Size(node, P(56), P(56));
                Kit.Round(node, P(28));
                Kit.Border(node, p.U(3), PK.Ink);
                if (t <= cur) Kit.Picture(node, GameArt.Radial("cp_node", .5f, .5f, .5f, .5f, (0f, GameTheme.C("#ffe36a")), (1f, GameTheme.C("#f2a600"))));
                else node.style.backgroundColor = GameTheme.T("#2a4a7a");
                PK.T(node, p, t.ToString(), 24);
                CpPassTile(col, c, t, false, tw, th);
            }

            // start scrolled so the current tier shows
            track.schedule.Execute(() => track.scrollOffset = new Vector2(Mathf.Max(0, (cur - 2) * colW), 0));
        }

        private void CpPassTile(VisualElement col, Ctx c, int tier, bool premium, float tw, float th)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var xp = CpXp;
            var (art, text) = CpPass.Reward(tier, premium);
            var got = CpPass.Claimed(tier, premium);
            var claim = CpPass.CanClaim(tier, premium, xp);
            var tile = PK.Centre(PK.Col(col));
            PK.Size(tile, tw, th);
            Kit.Nine(tile, GameArt.Get(premium ? "CardFrame06_Bg_Yellow" : "CardFrame06_Bg_Blue"), 28, 40, 28, 20, p.U(22) / 40f);
            var icon = PK.Img(tile, art, P(78));
            var val = PK.T(tile, p, text, 28);
            val.style.marginTop = P(6);
            if (got)
            {
                icon.style.unityBackgroundImageTintColor = new Color(.6f, .6f, .6f, 1);
                tile.style.unityBackgroundImageTintColor = new Color(.7f, .7f, .7f, 1);
                val.style.opacity = .7f;
                var ck = PK.Img(tile, "Toggle01_Check_Green", P(56));
                PK.Abs(ck, right: -tw * .08f, bottom: -th * .08f);
            }

            if (premium && !CpPass.Active)
            {
                var lk = PK.Img(tile, "Icon_ImageIcon_Lock01_s", P(52));
                PK.Abs(lk, right: -tw * .06f, bottom: -th * .06f);
            }

            if (claim)
            {
                var tag = PK.Centre(CpGradBox(col, "claimtag", GameTheme.C("#6fe03a"), GameTheme.C("#2fa81c"), p.U(3), p.U(10), 0));
                PK.Abs(tag, left: (P(p.Phone ? 176 : 212) - P(130)) / 2f, top: premium ? -P(30) : th + P(16) * 2 + P(56) - P(30), width: P(130), height: P(46));
                PK.T(tag, p, "Claim", 22);
                Action take = () =>
                {
                    if (!CpPass.CanClaim(tier, premium, CpXp)) return;
                    CpPassClaim(tier, premium);
                };
                PK.Tap(tag, p, take);
                PK.Tap(tile, p, take);
            }
        }

        private bool _cpPassClaiming;

        /// <summary>Collect a tier (pass-claim): the island is mirrored, the server checks the pass XP and pays.</summary>
        private async void CpPassClaim(int tier, bool premium)
        {
            var shop = Wallet.Shop;
            var host = Host;
            if (_cpPassClaiming || shop == null || host == null) return;
            if (!shop.Online)
            {
                Say("Connect to the internet to collect it");
                return;
            }
            _cpPassClaiming = true;
            var result = await shop.ClaimPass(tier, premium, JsonUtility.ToJson(host.CurrentState()));
            _cpPassClaiming = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code switch
                {
                    ErrorCode.PassClaimed => "Already collected",
                    ErrorCode.PassLocked => "Not reached yet",
                    ErrorCode.PassPremiumRequired => "Needs the Farm Pass",
                    _ => "Couldn't collect it — check your connection and try again",
                });
                MarkDirty();
                return;
            }
            var got = result.Value;
            if (got.Fresh && got.Coins > 0) Economy?.AddCoins((int)got.Coins);
            var prize = CpPass.Prize(tier, premium);
            Open("reward", CpPrize.Reward(got.Coins, got.Gems, got.Boosts, prize.Art, prize.Label));
        }

        /// <summary>The purple Popup_Pass: orange ribbon, glowing chest, four benefits, the price button.</summary>
        private void CpPassBuy(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var w = p.Phone ? 980 : 760;
            var box = PK.Col(c.Root, P(16));
            box.pickingMode = PickingMode.Position;
            box.style.alignItems = Align.Center;
            box.style.position = Position.Absolute;
            box.style.left = new Length(50, LengthUnit.Percent);
            box.style.top = new Length(50, LengthUnit.Percent);
            box.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-45, LengthUnit.Percent));
            box.style.width = P(w);
            PK.Pad(box, P(34), P(70), P(34), P(34));
            PK.Grad(box, "cp_purple", true, (0f, GameTheme.C("#9b4dff")), (1f, GameTheme.C("#6a1fd6")));
            Kit.Border(box, p.U(4), PK.Ink);
            box.style.borderBottomWidth = p.U(4) + p.U(8);
            Kit.Round(box, p.U(22));

            var rb = PK.Centre(PK.Abs(PK.Box(box), left: (P(w) - P(600)) / 2f - p.U(4), top: -P(50) - p.U(4), width: P(600), height: P(110)));
            Kit.Picture(rb, GameArt.Get("Title_Ribbon_Bg_Orange"));
            rb.style.paddingBottom = P(20);
            var rc = PK.Col(rb, P(4));
            rc.style.alignItems = Align.Center;
            PK.T(rc, p, "FARM PASS", 36);
            PK.N(rc, p, $"Season {CpPass.SeasonNumber} · {CpPass.SeasonDays} days", 15, GameTheme.C("#ffe9b0"));

            var x = PK.Centre(PK.Abs(PK.Box(box), top: -P(22), right: -P(24), width: P(70), height: P(77)));
            Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
            var xi = PK.Img(x, "Icon_PictoIcon_Close", P(70) * .52f * 53f / 49f, P(70) * .52f);
            xi.style.marginTop = -P(70) * .08f;
            PK.Tap(x, p, Back, "whoosh");

            var row = PK.Row(box, P(24));
            row.style.alignSelf = Align.Stretch;
            var chestBox = PK.Centre(PK.Box(row));
            PK.Size(chestBox, P(260), P(260) * 330f / 420f);
            var glow = PK.Abs(PK.Box(chestBox), left: -P(40), top: -P(40), width: P(340), height: P(260) * 330f / 420f + P(80));
            Kit.Contain(glow, GameArt.Get("Common_Popup_Glow"));
            glow.style.opacity = GameTheme.Fade(.7f);
            CpSpin(c, glow);
            var chest = PK.Inset(PK.Box(chestBox));
            Kit.Contain(chest, GameArt.Get("ShopItem_SpecialChest_Green"));
            var bens = PK.Col(row, P(10));
            bens.style.flexGrow = 1;
            bens.style.flexShrink = 1;
            foreach (var perk in Wallet.Prices.Pass.PremiumPerks)
            {
                var (icon, text) = (perk.Icon, perk.Text);
                var b = PK.Row(bens, P(12));
                PK.Pad(b, P(12), P(8), P(12), P(8));
                b.style.backgroundColor = new Color(40 / 255f, 0, 90 / 255f, .4f);
                Kit.Round(b, p.U(12));
                PK.Img(b, icon, P(44));
                var l = PK.N(b, p, text, 19, Color.white, TextAnchor.MiddleLeft, true);
                l.style.flexShrink = 1;
            }

            var active = Wallet.Shop?.Pass is { PremiumActive: true };
            if (active) PK.T(box, p, "Premium is yours this season!", 28);
            PassBuyButtons(box, p, active);
            var saved = Wallet.Shop?.PassCredits ?? 0;
            var note = PK.N(box, p, (saved > 0 ? (saved == 1 ? "1 Farm Pass is" : saved + " Farm Passes are") + " saved for next season. " : "")
                + "A pass used while premium is on waits for the next season · free-track rewards keep unlocking as you play.", 14, GameTheme.C("#e3d0ff"), TextAnchor.MiddleCenter, true);
            note.style.alignSelf = Align.Stretch;
        }
    }
}
