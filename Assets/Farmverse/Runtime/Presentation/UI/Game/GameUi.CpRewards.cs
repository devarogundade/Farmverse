#nullable enable
using System;
using System.Linq;
using Farmverse.Contracts;
using System.Collections.Generic;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Reward moments (rewards.html): Daily Bonus (Popup_RewardWeek), Lucky Wheel (Roulette), Reward reveal
    // (Popup_RewardGet) and Level up (LevelUp). Phone k 0.5. Welcome back belongs to the gameplay screens.
    public sealed partial class GameUi
    {
        private static readonly bool RegCpDaily = Register("daily", UiLayer.Popup, (ui, c) => ui.CpDaily(c), .5f);
        private static readonly bool RegCpWheel = Register("wheel", UiLayer.Popup, (ui, c) => ui.CpWheel(c), .5f);
        private static readonly bool RegCpReward = Register("reward", UiLayer.Popup, (ui, c) => ui.CpRewardPopup(c), .5f);
        private static readonly bool RegCpLevelUp = Register("levelup", UiLayer.Popup, (ui, c) => ui.CpLevelUp(c), .5f);

        /// <summary>Shows the level-up ceremony for this level (the gameplay HUD calls it when the island levels up).</summary>
        public void CpShowLevelUp(int level) => Open("levelup", level);

        // ------------------------------------------------------------------ daily bonus

        /// <summary>The ladder's prizes (the catalogue's dailyLogin, days 1–7).</summary>
        private static IReadOnlyList<Bundle> CpDays => Wallet.Prices.DailyLogin;

        /// <summary>
        /// The daily ladder as the server keeps it (state/daily): which day (1–7) is today's, and whether it's claimed.
        /// Missing days pause, never reset. Before the server has answered, nothing is offered.
        /// </summary>
        public static (int day, bool claimedToday) CpDailyState()
        {
            var d = Wallet.Shop?.Daily;
            if (Wallet.Shop == null || !Wallet.Shop.Online) return (d?.LoginDay ?? 1, true);
            if (d == null) return (1, false);
            var claimed = d.LoginOn == ShopUtcDay;
            if (!claimed) return (Mathf.Clamp(d.LoginDay, 1, 7), false);
            return (d.LoginDay == 1 ? 7 : d.LoginDay - 1, true);   // show the day just claimed
        }

        /// <summary>A daily bonus is waiting (for the Home badge).</summary>
        public static bool CpDailyReady => !CpDailyState().claimedToday;

        private bool _cpClaiming;

        /// <summary>Collect today's login reward (daily-claimLogin): the server pays it; coins land on the island.</summary>
        private void CpDailyClaim() => CpDailyClaimAsync(false);

        /// <summary>Collect it, then watch an ad for the same prize again (ads.daily_double).</summary>
        private void CpDailyClaimDouble() => CpDailyClaimAsync(true);

        private async void CpDailyClaimAsync(bool doubled)
        {
            var shop = Wallet.Shop;
            if (_cpClaiming || shop == null) return;
            if (!shop.Online)
            {
                Say("Connect to the internet to collect it");
                return;
            }
            _cpClaiming = true;
            var result = await shop.ClaimLogin();
            _cpClaiming = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code == ErrorCode.DailyClaimed ? "Come back tomorrow" : "Couldn't collect it — check your connection and try again");
                MarkDirty();
                return;
            }
            var got = result.Value;
            if (got.Fresh && got.Coins > 0) Economy?.AddCoins((int)got.Coins);
            var day = CpDays.Count >= got.Day ? CpDays[(int)got.Day - 1] : null;
            if (!doubled)
            {
                Replace("reward", CpPrize.Reward(got.Coins, got.Gems, got.Boosts, day?.Art, day?.Label));
                return;
            }
            WatchAd("daily_double",
                more =>
                {
                    var boosts = new System.Collections.Generic.Dictionary<string, long>(got.Boosts);
                    foreach (var kv in more.Boosts) boosts[kv.Key] = (boosts.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                    var r = CpPrize.Reward(got.Coins + more.Coins, got.Gems + more.Gems, boosts, day?.Art, day?.Label);
                    r.Title = "REWARDS ×2";
                    Replace("reward", r);
                },
                () => Replace("reward", CpPrize.Reward(got.Coins, got.Gems, got.Boosts, day?.Art, day?.Label)));
        }

        private void CpDaily(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var (today, claimedToday) = CpDailyState();
            float w = P(220), h = P(200), g = P(16);
            var pop = PK.Popup(c.Root, p, 3 * 220 + 2 * 16 + 250 + 16 + 60, 0, "Daily Bonus", Back, "Green", 30, 560);

            var body = PK.Row(pop, g);
            body.style.alignItems = Align.Stretch;
            body.style.marginTop = P(14);
            body.style.alignSelf = Align.Center;
            var grid = PK.Box(body);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            PK.Size(grid, 3 * w + 2 * g, 2 * h + g);
            for (var i = 0; i < 6; i++)
            {
                var d = i + 1;
                var got = d < today || (d == today && claimedToday);
                var isT = d == today && !claimedToday;
                var (art, text) = i < CpDays.Count ? CpPrize.Tile(CpDays[i]) : ("Icon_Gift", "");
                var cell = PK.Centre(PK.Col(grid));
                PK.Size(cell, w, h);
                if (i % 3 != 0) cell.style.marginLeft = g;
                if (i >= 3) cell.style.marginTop = g;
                Kit.Nine(cell, GameArt.Get("CardFrame06_Bg_Blue"), 28, 40, 28, 20, p.U(20) / 40f);
                if (isT)
                {
                    var ring = PK.Inset(PK.Box(cell), -p.U(6), -p.U(6), -p.U(6), -p.U(6));
                    Kit.Border(ring, p.U(4), Color.white);
                    Kit.Round(ring, p.U(14));
                    PK.Tap(cell, p, CpDailyClaim);
                }

                var dl = PK.T(cell, p, "DAY " + d, 18);
                PK.Abs(dl, left: w * .08f, top: h * .06f);
                var ic = PK.Img(cell, art, P(84));
                ic.style.marginTop = P(14);
                var v = PK.T(cell, p, text, 28);
                v.style.marginTop = P(8);
                if (got)
                {
                    ic.style.opacity = .35f;
                    v.style.opacity = .35f;
                    var st = PK.Inset(PK.Box(cell), w * .08f, h * .08f, w * .08f, h * .08f);
                    Kit.Contain(st, GameArt.Get("Icon_ImageIcon_ClearStamp_l"));
                    st.style.opacity = .95f;
                }

                if (isT || (d == today + 1 && !isT))
                    CpTag(cell, p, isT ? "Today" : "Tomorrow", w);
            }

            // day 7: the tall purple card with the big chest
            var d7 = PK.Centre(PK.Col(body));
            PK.Size(d7, P(250), 2 * h + g);
            Kit.Nine(d7, GameArt.Get("CardFrame06_Bg_Purple"), 28, 40, 28, 20, p.U(20) / 40f);
            var d7l = PK.T(d7, p, "DAY 7", 20);
            PK.Abs(d7l, left: P(250) * .08f, top: (2 * h + g) * .06f);
            var day7 = CpDays.Count >= 7 ? CpDays[6] : null;
            var chestTex = GameArt.Get(day7?.Art ?? "ShopItem_SpecialChest_Purple");
            var cw = P(250) * .88f;
            var chest = PK.Box(d7);
            PK.Size(chest, cw, chestTex != null && chestTex.width > 0 ? cw * chestTex.height / chestTex.width : cw);
            Kit.Picture(chest, chestTex);
            var gemRow = PK.Row(d7, P(8));
            gemRow.style.marginTop = P(6);
            if (day7 != null && day7.Gems > 0)
            {
                PK.Img(gemRow, "ResourceBar_Icon_Gem", P(36));
                PK.T(gemRow, p, PK.Num(day7.Gems), 26);
            }
            else if (day7 != null && day7.Coins > 0)
            {
                PK.Img(gemRow, "ResourceBar_Icon_Gold", P(36));
                PK.T(gemRow, p, PK.Num(day7.Coins), 26);
            }
            var bc = PK.T(d7, p, day7?.Label ?? "Big chest", 22);
            bc.style.marginTop = P(6);
            if (today == 7 && claimedToday) d7.style.opacity = .6f;
            if (today == 7 && !claimedToday)
            {
                var ring = PK.Inset(PK.Box(d7), -p.U(6), -p.U(6), -p.U(6), -p.U(6));
                Kit.Border(ring, p.U(4), Color.white);
                Kit.Round(ring, p.U(14));
                CpTag(d7, p, "Today", P(250));
                PK.Tap(d7, p, CpDailyClaim);
            }

            var btns = PK.Row(pop, P(20));
            btns.style.justifyContent = Justify.Center;
            btns.style.marginTop = P(22);
            if (claimedToday) PK.Btn(btns, p, "gray", "Come back tomorrow", 360, 74, 26);
            else
            {
                PK.Btn(btns, p, "green", "Claim", 240, 74, 30, CpDailyClaim);
                var x2 = PK.Btn(btns, p, "yellow", null, 260, 74, 30, CpDailyClaimDouble);
                PK.Img(x2, "Icon_ImageIcon_Ad_00_s", P(74) * .62f).style.marginRight = P(30) * .3f;
                PK.T(x2, p, "Claim ×2", 30);
            }

            var note = PK.N(pop, p, "Missing a day pauses your streak — it never resets.", 15, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter);
            note.style.marginTop = P(10);
        }

        /// <summary>The gold "Today" / "Tomorrow" tag over a day card.</summary>
        private static void CpTag(VisualElement cell, Pt p, string text, float cellW)
        {
            var tag = CpGradBox(cell, "daytag", GameTheme.C("#ffe36a"), GameTheme.C("#f2a600"), p.U(3), p.U(8), 0);
            PK.Pad(tag, p.P(10), p.P(2), p.P(10), p.P(2));
            var l = PK.T(tag, p, text, 16);
            tag.style.position = Position.Absolute;
            tag.style.top = -p.P(22);
            tag.RegisterCallback<GeometryChangedEvent>(_ => tag.style.left = (cellW - tag.layout.width) / 2f);
        }

        // ------------------------------------------------------------------ lucky wheel

        /// <summary>The wheel's eight slices (the catalogue's wheel; the server rolls them by weight).</summary>
        private static (string art, string text)[] CpSegs => Wallet.Prices.Wheel.Segments.Select(sg => CpPrize.Tile(sg.Prize)).ToArray();

        private static int CpWheelSpinsADay => Wallet.Prices.Wheel.SpinsPerDay;

        private static float CpWheelRot, CpWheelFrom, CpWheelTo, CpWheelStart = -1;
        private static int CpWheelPrize = -1;
        private static WheelSpinResponse? CpWheelWon;
        private bool _cpSpinning;

        /// <summary>A new run starts with the wheel at rest (the Editor's Play without a domain reload keeps statics).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CpWheelReset()
        {
            CpWheelRot = CpWheelFrom = CpWheelTo = 0;
            CpWheelStart = -1;
            CpWheelPrize = -1;
            CpWheelWon = null;
        }

        /// <summary>Spins left today (state/daily): each one is paid by watching a rewarded ad.</summary>
        public static int CpSpinsLeft
        {
            get
            {
                var d = Wallet.Shop?.Daily;
                if (d == null || d.WheelOn != ShopUtcDay) return CpWheelSpinsADay;
                return Mathf.Max(0, CpWheelSpinsADay - d.WheelUsed);
            }
        }

        /// <summary>
        /// Spin: watch a rewarded ad, then wheel-spin under the ad's key (the server checks the ad, rolls the slice
        /// and pays it); the wheel then turns to it. No free spins, no extra spins past the day's limit.
        /// </summary>
        private async void CpSpin()
        {
            if (CpWheelStart >= 0 || _cpSpinning || _adBusy) return;
            var shop = Wallet.Shop;
            var ads = Ads;
            if (shop == null || !shop.Online || ads == null || !ads.Online)
            {
                Say("Connect to the internet to spin");
                return;
            }
            if (CpSpinsLeft <= 0)
            {
                Say("No spins left today — come back tomorrow");
                return;
            }
            _cpSpinning = true;
            _adBusy = true;
            MarkDirty();
            var result = await ads.WatchThen(key => shop.Spin(key));
            _cpSpinning = false;
            _adBusy = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code switch
                {
                    ErrorCode.Cancelled => "Watch the ad to the end to spin",
                    ErrorCode.Unsupported => "No ad right now — try again in a moment",
                    ErrorCode.WheelNoSpins => "No spins left today — come back tomorrow",
                    ErrorCode.AdNotVerified => "The ad is still being checked — try again in a minute",
                    _ => "Couldn't spin — check your connection and try again",
                });
                MarkDirty();
                return;
            }
            var k = Mathf.Clamp((int)result.Value.Segment, 0, 7);
            CpWheelWon = result.Value;
            CpWheelPrize = k;
            CpWheelFrom = CpWheelRot;
            CpWheelTo = CpWheelRot + 360 * 5 + (360 - (k * 45 + 22.5f)) - CpWheelRot % 360;
            CpWheelStart = Time.unscaledTime;
            Farmverse.Presentation.World.Island.IslandAudio.Play("whoosh");
        }

        private void CpWheel(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var D = P(560);
            var R = D / 2f;
            c.Watch(() => CpSpinsLeft);

            var rb = PK.Centre(PK.Abs(PK.Box(c.Root), left: (p.W - P(520)) / 2f, top: P(p.Phone ? 20 : 70), width: P(520), height: P(100)));
            Kit.Picture(rb, GameArt.Get("Title_Ribbon_Bg_Orange"));
            rb.style.paddingBottom = P(18);
            PK.T(rb, p, "Lucky Wheel", 36);

            var x = PK.Centre(PK.Abs(PK.Box(c.Root), top: P(24), right: p.R + P(30), width: P(70), height: P(77)));
            Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
            var xi = PK.Img(x, "Icon_PictoIcon_Close", P(70) * .52f * 53f / 49f, P(70) * .52f);
            xi.style.marginTop = -P(70) * .08f;
            PK.Tap(x, p, Back, "whoosh");

            var wheel = PK.Abs(PK.Box(c.Root), left: (p.W - D) / 2f, top: p.H / 2f + P(40) - R, width: D, height: D);
            var rim = PK.Inset(PK.Box(wheel), -P(30), -P(30), -P(30), -P(30));
            Kit.Picture(rim, GameArt.Get("Play_Roulette_Bg"));
            var disc = PK.Inset(PK.Box(wheel));
            Kit.Picture(disc, CpArt.Wheel(8f / 280f, 5f / 280f));
            for (var i = 0; i < CpSegs.Length; i++)
            {
                var a = (i * 45 + 22.5f) * Mathf.Deg2Rad;
                var cx = R + Mathf.Sin(a) * R * .62f;
                var cy = R - Mathf.Cos(a) * R * .62f;
                var seg = PK.Centre(PK.Col(disc));
                var sw = P(110);
                var sh = P(62) + P(24);
                PK.Abs(seg, left: cx - sw / 2f, top: cy - sh / 2f, width: sw, height: sh);
                seg.style.rotate = new Rotate(new Angle(i * 45 + 22.5f, AngleUnit.Degree));
                PK.Img(seg, CpSegs[i].art, P(62));
                PK.T(seg, p, CpSegs[i].text, 24);
            }

            var arrowTex = GameArt.Get("Play_Roulette_Arrow");
            var ah = P(110);
            var aw = arrowTex != null && arrowTex.height > 0 ? ah * arrowTex.width / arrowTex.height : ah;
            var arrow = PK.Abs(PK.Box(wheel), left: R - aw / 2f, top: -P(50), width: aw, height: ah);
            Kit.Picture(arrow, arrowTex);
            var spin = PK.Centre(PK.Abs(PK.Box(wheel), left: R - P(85), top: R - P(91), width: P(170), height: P(182)));
            Kit.Picture(spin, GameArt.Get("Play_Roulette_Btn_spin"));
            PK.T(spin, p, "SPIN!", 32);
            PK.Tap(spin, p, CpSpin, "");

            c.Live(() =>
            {
                if (CpWheelStart >= 0)
                {
                    var t = Mathf.Clamp01((Time.unscaledTime - CpWheelStart) / 3.2f);
                    var e = 1f - Mathf.Pow(1f - t, 3f);   // ease out (cubic-bezier(.17,.67,.2,1))
                    CpWheelRot = Mathf.Lerp(CpWheelFrom, CpWheelTo, e);
                    if (t >= 1f)
                    {
                        CpWheelStart = -1;
                        var (_, text) = CpSegs[CpWheelPrize];
                        var won = CpWheelWon;
                        CpWheelWon = null;
                        // gems and boosts are already on the server; the coins are the island's
                        if (won != null && won.Fresh && won.Coins > 0) Economy?.AddCoins((int)won.Coins);
                        Say($"You won {text}!", 1.5f);
                        Farmverse.Presentation.World.Island.IslandAudio.Play("coin");
                        MarkDirty();
                    }
                }

                disc.style.rotate = new Rotate(new Angle(CpWheelRot, AngleUnit.Degree));
            });

            // spins left (right side)
            var info = PK.Col(c.Root, P(14));
            info.style.alignItems = Align.Center;
            PK.Abs(info, right: p.R + P(60), top: p.H / 2f);
            info.style.translate = new Translate(0, new Length(-50, LengthUnit.Percent));
            var pill = CpGradBox(info, "spinpill", GameTheme.C("#ffe36a"), GameTheme.C("#f2a600"), p.U(3), p.U(10), 0);
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.height = P(70);
            PK.Pad(pill, P(18), 0, P(18), 0);
            PK.Gap(pill, P(10));
            PK.Img(pill, "Icon_ImageIcon_Ad_00_s", P(46));
            var pc = PK.Col(pill, P(4));
            PK.T(pc, p, "Spins today", 18);
            PK.T(pc, p, $"{CpSpinsLeft} / {CpWheelSpinsADay}", 26);
            var n = PK.N(info, p, CpSpinsLeft > 0
                ? $"Watch a short ad for each spin.\n{CpWheelSpinsADay} spins a day, reset at midnight (UTC)."
                : $"That's all {CpWheelSpinsADay} spins for today.\nCome back tomorrow!", 15, GameTheme.T("#dff1ff"), TextAnchor.MiddleCenter, true);
            n.style.maxWidth = P(220);
        }

        // ------------------------------------------------------------------ reward reveal

        private void CpRewardPopup(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var reward = c.Args switch
            {
                CpReward r => r,
                ValueTuple<string, string>[] arr => new CpReward(arr),
                string[] pairs => CpFromPairs(pairs),
                _ => new CpReward(),
            };

            var tapAny = PK.Inset(PK.Box(c.Root));
            PK.Tap(tapAny, p, Back, "");
            var gs = P(900);
            var glow = PK.Abs(PK.Box(c.Root), left: (p.W - gs) / 2f, top: (p.H - gs) / 2f, width: gs, height: gs);
            Kit.Contain(glow, GameArt.Get("Common_Popup_Glow"));
            glow.style.opacity = GameTheme.Fade(.25f);
            CpSpin(c, glow);

            var rb = PK.Centre(PK.Abs(PK.Box(c.Root), left: (p.W - P(600)) / 2f, top: P(p.Phone ? 40 : 170), width: P(600), height: P(110)));
            Kit.Picture(rb, GameArt.Get("Title_Ribbon_Bg_Green"));
            rb.style.paddingBottom = P(20);
            PK.T(rb, p, reward.Title, 40);

            var items = PK.Row(c.Root, P(40));
            PK.Abs(items, left: 0, right: 0, top: p.H / 2f);
            items.style.justifyContent = Justify.Center;
            items.style.translate = new Translate(0, new Length(-50, LengthUnit.Percent));
            var phase = 0f;
            foreach (var (art, text) in reward.Items)
            {
                var it = PK.Col(items);
                it.style.alignItems = Align.Center;
                var disc = PK.Centre(PK.Box(it));
                PK.Size(disc, P(170), P(170));
                Kit.Picture(disc, CpArt.SoftDisc());
                PK.Img(disc, art, P(130));
                PK.T(it, p, text, 40);
                CpPulse(c, it, phase);
                phase += .2f;
            }

            var ok = PK.Btn(c.Root, p, "blue", "OK", 260, 80, 34, Back);
            PK.Abs(ok, left: (p.W - P(260)) / 2f, bottom: p.B + P(p.Phone ? 40 : 150));
            var hint = PK.N(c.Root, p, "Tap anywhere to continue", 18, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter);
            PK.Abs(hint, left: 0, right: 0, bottom: p.B + P(p.Phone ? 6 : 100));
        }

        private static CpReward CpFromPairs(string[] pairs)
        {
            var r = new CpReward();
            foreach (var s in pairs)
            {
                var i = s.IndexOf('|');
                if (i > 0) r.Items.Add((s.Substring(0, i), s.Substring(i + 1)));
            }

            return r;
        }

        // ------------------------------------------------------------------ level up

        private void CpLevelUp(Ctx c)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var level = c.Args is int l ? l : Economy?.BestLevel ?? 1;

            var bg = PK.Inset(PK.Box(c.Root));
            bg.pickingMode = PickingMode.Position;
            bg.style.backgroundColor = GameTheme.T("#062050");
            var glow = PK.Inset(PK.Box(bg));
            Kit.Picture(glow, GameArt.Radial("cp_lvglow_" + GameTheme.Current, .5f, .42f, .6f, .55f,
                (0f, GameTheme.T("#8cf0ff", .85f)), (.45f, GameTheme.T("#288ce6", .35f)), (.75f, GameTheme.T("#061e50", .95f)), (1f, GameTheme.T("#061e50", .95f))));
            var rs = p.W * 2f;
            var rays = PK.Abs(PK.Box(bg), left: (p.W - rs) / 2f, top: p.H * .42f - rs / 2f, width: rs, height: rs);
            Kit.Picture(rays, CpArt.Rays());
            CpSpin(c, rays, 30f);

            var head = PK.T(c.Root, p, "Your island reached a new level!", 32, null, true, TextAnchor.MiddleCenter);
            PK.Abs(head, left: 0, right: 0, top: P(p.Phone ? 24 : 80));
            var badge = PK.Centre(PK.Abs(PK.Box(c.Root), left: p.W / 2f - P(120), top: P(p.Phone ? 90 : 180), width: P(240), height: P(266)));
            Kit.Contain(badge, GameArt.Get("Slider_Level01_Badge_Blue"));
            PK.T(badge, p, level.ToString(), 110);
            CpPulse(c, badge);
            var up = PK.T(c.Root, p, "LEVEL UP!", 80, null, true, TextAnchor.MiddleCenter);
            PK.Abs(up, left: 0, right: 0, top: P(p.Phone ? 370 : 470));

            // what the level gave (coins, basket room) and what it opened in the shop
            var gave = new List<(string art, string text)>();
            var coins = GameplayRules.LevelReward(level);
            if (coins > 0) gave.Add(("ResourceBar_Icon_Gold", PK.Num(coins)));
            if (level % 5 == 0) gave.Add(("Icon_Chest", "+2 room"));
            var opened = new List<(string art, string text)>();
            var cat = Sandbox != null ? Sandbox.Catalogue : null;
            if (cat != null)
                foreach (var e in cat.entries)
                    if (GameplayRules.UnlockLevel(e.id) == level && opened.Count < 4)
                        opened.Add(("Icon_Hammer", string.IsNullOrEmpty(e.displayName) ? e.id : e.displayName));

            var row = PK.Row(c.Root, P(60));
            row.style.alignItems = Align.FlexStart;
            row.style.justifyContent = Justify.Center;
            PK.Abs(row, left: 0, right: 0, top: P(p.Phone ? 480 : 600));
            if (gave.Count > 0) CpTiles(row, p, "REWARDS", gave, 100, 22);
            if (opened.Count > 0) CpTiles(row, p, "NEW IN THE SHOP", opened, 130, 18);

            var go = PK.Btn(c.Root, p, "green", "Continue", 260, 80, 32, () =>
            {
                Economy?.TakeLevelCoins();
                Back();
            });
            PK.Abs(go, right: p.R + P(40), bottom: p.B + P(40));
        }

        private static void CpTiles(VisualElement row, Pt p, string title, List<(string art, string text)> items, float w, float fs)
        {
            var col = PK.Col(row, p.P(12));
            col.style.alignItems = Align.Center;
            PK.T(col, p, title, 20, GameTheme.T("#9fe8ff"));
            var tiles = PK.Row(col, p.P(12));
            foreach (var (art, text) in items)
            {
                var t = PK.Centre(CpGradBox(tiles, "tile2", GameTheme.T("#123f7c"), GameTheme.T("#0e3366"), p.U(3), p.U(16), 0));
                PK.Size(t, p.P(w), p.P(110));
                PK.Img(t, art, p.P(56));
                PK.T(t, p, text, fs);
            }
        }
    }
}
