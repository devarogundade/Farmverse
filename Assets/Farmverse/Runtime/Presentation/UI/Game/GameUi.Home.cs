#nullable enable
using System;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Home (the pack's Lobby), to home.html: level + XP top-left with the Daily gift, coins · gems ·
    /// settings top-right, the Goal and Farm Pass cards, the menu grid (2×3 tablet, 3×2 phone), the island
    /// status chip, the Shop · Storage · Orders tabs and the COUPONS · BUILD buttons over a static picture
    /// of the island. A new player first meets Grandpa Joe (three lines), then a hand points at BUILD.
    /// Tapping the island picture goes to the island.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegHome_Home = Register("home", UiLayer.Base, (ui, c) => ui.HomeScreen(c), .54f, covers: true);

        private const string HomeTutorialPref = "fv_home_tutorial";   // 0 talk, 1 hand on BUILD, 2 done

        private static readonly string[] HomeLines =
        {
            "Welcome to your island, farmer! I'm Grandpa Joe.",
            "This little island is all yours. Let's turn it into the best farm in the sea!",
            "First things first — let's build a field. Tap <color=#e08c00>BUILD</color>!",
        };

        private int _homeLine;

        private static string HomeSeasonArt(string season) => season switch
        {
            "Spring" => "G_Flower",
            "Summer" => "G_Sunny",
            "Winter" => "G_Water",
            _ => "G_Autumn",
        };

        private static string HomeWeatherArt(GameplayRules.WeatherKind w) => w switch
        {
            GameplayRules.WeatherKind.Rain => "G_Rain",
            GameplayRules.WeatherKind.Storm => "G_Storm",
            _ => "G_Sunny",
        };

        private void HomeScreen(Ctx c)
        {
            var p = c.P;
            var root = c.Root;
            if (Sandbox != null) Sandbox.Lobby = true;
            if (Farmverse.Presentation.World.Island.IslandShadow.JustRestored)
            {
                Farmverse.Presentation.World.Island.IslandShadow.JustRestored = false;
                Say("Welcome back! Your farm is restored.");
            }
            var eco = Economy;
            var tut = PlayerPrefs.GetInt(HomeTutorialPref, 0);
            var newPlayer = tut < 2;
            float k = p.K / p.Unit;   // the design's k (1 tablet, .54 phone)
            float P(float n) => p.P(n);
            float L = p.L, R = p.R, B = p.B, m = P(16);
            float xL = L + m, xR = R + m;

            // the island picture (static art); tapping it goes to the island
            var bg = PK.Inset(PK.Box(root));
            Kit.Cover(bg, GameArt.Get("Home_Bg"));
            PK.Tap(bg, p, () => Go("play"), "whoosh");

            // ---------------------------------------------------------------- level, name, xp, daily gift
            var xpBar = PK.Abs(PK.Box(root), left: xL + P(60), top: P(44), width: P(320), height: P(34));
            xpBar.style.backgroundColor = GameTheme.T("#0d1d4f");
            Kit.Border(xpBar, p.U(3), PK.Ink);
            Kit.Round(xpBar, p.U(8));
            PK.Clip(xpBar);
            var xpFill = PK.Abs(PK.Box(xpBar), left: 0, top: 0, bottom: 0);
            HomeNine(xpFill, p, "Slider_Level01_Fill_Blue", 14, 14, 14, 14, 6);
            var xpText = HomeTS(PK.Centre(PK.Inset(PK.Box(xpBar))), p, "", 20, 3.5f);

            var badge = PK.Img(root, "Slider_Level01_Badge_Blue", P(86));
            PK.Abs(badge, left: xL, top: P(10));
            var lvlBox = PK.Centre(PK.Abs(PK.Box(root), left: xL, top: P(10), width: P(78), height: P(80)));
            var lvl = HomeTS(lvlBox, p, "1", 40, 5);
            HomeTAt(root, p, xL + P(92), P(14), PlayerName, 24, 4);
            PK.Tap(lvlBox, p, () => Open("profile"));
            lvlBox.pickingMode = PickingMode.Position;

            c.Live(() =>
            {
                var e = Economy;
                var level = e?.IslandLevel ?? 1;
                long need = GameplayRules.XpToNext(level), have = e != null ? e.Xp - GameplayRules.XpAt(level) : 0;
                lvl.text = Loc.Of(level.ToString());
                xpText.text = Loc.Of($"{have}/{need}");
                PK.SetPct(xpFill, need > 0 ? 100f * have / need : 0);
            });

            if (!newPlayer)
            {
                var gift = PK.Img(root, "Icon_ImageIcon_Gift_Purple", P(70));
                PK.Abs(gift, left: xL + P(395), top: P(14));
                PK.Tap(gift, p, () => Open("daily"));
                if (HomeDailyReady) HomePulse(c, gift, 1.08f, 1.4f);
            }

            // ---------------------------------------------------------------- currencies and settings
            var pills = PK.Abs(PK.Row(root), right: xR + P(92), top: P(22), height: P(50));   // fit to their numbers, left of the gear
            pills.pickingMode = PickingMode.Ignore;
            Label HomePill(bool gem, Action plus)
            {
                var pill = PK.Row(pills);
                pill.style.height = P(50);
                pill.style.flexShrink = 0;
                pill.style.justifyContent = Justify.FlexEnd;
                pill.style.paddingLeft = p.U(4) + P(42) + P(14);   // clear of the plus button
                pill.style.paddingRight = P(64);                    // clear of the hanging icon
                Kit.Round(pill, p.U(10));
                PK.Grad(pill, gem ? "homepillgem" : "homepillgold", true, gem
                    ? new[] { (0f, GameTheme.C("#170a2c")), (1f, GameTheme.C("#221040")) }
                    : new[] { (0f, GameTheme.C("#170d06")), (1f, GameTheme.C("#2b160a")) });
                var pl = PK.Img(pill, gem ? "ResourceBar_Btn_Single_Purple" : "ResourceBar_Btn_Single_Yellow", P(42));
                PK.Abs(pl, left: p.U(4), top: (P(50) - P(42)) / 2f);
                PK.Tap(pl, p, plus);
                var v = HomeTS(pill, p, "0", 26, 3.5f);
                var ico = PK.Img(pill, gem ? "ResourceBar_Icon_Gem" : "ResourceBar_Icon_Gold", P(62));
                PK.Abs(ico, right: -P(10), top: (P(50) - P(62)) / 2f);
                return v;
            }

            var coins = HomePill(false, () => Open("shop", "coins"));
            var gems = HomePill(true, () => Open("shop", "gems"));
            gems.parent.style.marginLeft = P(22);
            c.Live(() =>
            {
                coins.text = Loc.Of(PK.Num(Economy?.Coins ?? 0));
                gems.text = Loc.Of(PK.Num(Wallet.Gems));
            });

            var gear = PK.Centre(PK.Abs(PK.Box(root), right: 0, top: P(6), width: R + P(84), height: P(90)));
            gear.style.paddingRight = R;
            Kit.Flush(gear, false);
            PK.Img(gear, "Icon_PictoIcon_Setting", P(50)).style.marginLeft = P(10);
            PK.Tap(gear, p, () => Open("settings"));

            // ---------------------------------------------------------------- left cards
            var goal = eco?.CurrentGoal;
            c.Watch(() => Economy?.GoalIndex ?? -1);
            var card = PK.Abs(PK.Box(root), left: xL, top: P(122), width: P(390), height: P(112));
            HomeNine(card, p, "BannerFrame01_Single_Navy", 24, 24, 28, 24, 14);
            PK.Abs(PK.Img(card, "Icon_Quest", P(104)), left: P(4), top: -P(10));
            HomeTAt(card, p, P(120), P(16), "GOAL", 22, 3.5f, GameTheme.T("#7fe0ff"));
            var goalText = HomeTAt(card, p, P(190), P(16), goal?.Text ?? "All goals done!", 22, 3.5f);
            goalText.style.width = P(190);
            goalText.style.overflow = Overflow.Hidden;
            goalText.style.textOverflow = TextOverflow.Ellipsis;
            var gBar = PK.Abs(PK.Box(card), left: P(120), top: P(54), width: P(200), height: P(32));
            gBar.style.backgroundColor = GameTheme.T("#0d1d4f");
            Kit.Border(gBar, p.U(3), PK.Ink);
            Kit.Round(gBar, p.U(8));
            PK.Clip(gBar);
            var gFill = PK.Abs(PK.Box(gBar), left: 0, top: 0, bottom: 0);
            HomeNine(gFill, p, "Slider_Basic02_Fill_Yellow", 18, 16, 18, 16, 6);
            var gText = HomeTS(PK.Centre(PK.Inset(PK.Box(gBar))), p, "", 18, 3);
            PK.Abs(PK.Img(card, "ResourceBar_Icon_Gold", P(46)), left: P(326), top: P(46));
            HomeTAt(card, p, P(330), P(84), goal != null ? PK.Num(goal.Reward) : "", 16, 3);
            VisualElement? goalBadge = null;
            if (!newPlayer)
            {
                goalBadge = PK.Badge(card, p, "1", false, 30);
                PK.Abs(goalBadge, left: P(84), top: -P(8));
            }

            c.Live(() =>
            {
                var e = Economy;
                var g = e?.CurrentGoal;
                var have = g != null && e != null ? Mathf.Min(e.GoalHave(g), g.Need) : 0;
                var need = g?.Need ?? 1;
                gText.text = Loc.Of(g != null ? $"{have} / {need}" : "");
                PK.SetPct(gFill, 100f * have / need);
                if (goalBadge != null) goalBadge.style.display = g != null && e != null && e.GoalDone(g) ? DisplayStyle.Flex : DisplayStyle.None;
            });
            PK.Tap(card, p, () => Open("orders", "goals"));

            var pass = PK.Abs(PK.Box(root), left: xL, top: P(250), width: P(470), height: P(130));
            HomeNine(pass, p, "BannerFrame01_Single_Navy", 24, 24, 28, 24, 14);
            HomeTAt(pass, p, P(24), P(22), "FARM PASS", 24, 3.5f, GameTheme.C("#2fe0b0"));
            HomeTAt(pass, p, P(170), P(22), $"{(newPlayer ? 0 : CpPass.TierXp(CpXp))} / {CpPass.XpPerTier}", 24, 3.5f);
            var pr = PK.Abs(PK.Row(pass, P(6)), left: P(22), top: P(66));
            var coin = PK.Centre(PK.Box(pr));
            PK.Size(coin, P(40), P(40));
            Kit.Round(coin, P(20));
            Kit.Border(coin, p.U(3), PK.Ink);
            Kit.Picture(coin, GameArt.Radial("homepasscoin", .5f, .5f, .5f, .5f, (0f, GameTheme.C("#ffe36a")), (1f, GameTheme.C("#f2a600"))));
            HomeTS(coin, p, CpPass.Tier(CpXp).ToString(), 22, 3);
            var seg = PK.Row(pr, P(2));
            var passSteps = newPlayer ? 0 : CpPass.TierXp(CpXp) * 10 / Math.Max(1, CpPass.XpPerTier);
            for (var i = 0; i < 10; i++)
            {
                var b = PK.Box(seg);
                PK.Size(b, P(22), P(28));
                Kit.Round(b, p.U(2));
                b.style.backgroundColor = i < passSteps ? GameTheme.C("#ffd23a") : PK.Ink;
            }

            PK.Abs(PK.Img(pass, "ShopItem_SpecialChest_Green", P(118)), right: -P(14), top: -P(6));
            PK.Tap(pass, p, () => Open("pass"));

            // ---------------------------------------------------------------- menu grid
            c.Watch(() => NewsUnread);   // news arriving while home is up lights the badge
            var cols = p.Phone ? 3 : 2;
            float tileS = P(112), gap = P(14), gridTop = P(150);
            c.Watch(() => CpSpinsLeft);
            c.Watch(() => HomeDailyReady);
            var spinsLeft = CpSpinsLeft;
            var tiles = new (string icon, string label, Action go)[]
            {
                ("Icon_MenuIcon04_Trophy", "Leaderboard", () => Open("leaderboard")),
                ("Icon_MenuIcon04_Reward", "Daily", () => Open("daily")),
                ("Play_Roulette_wheel", "Wheel", () => Open("wheel")),
                ("Icon_MenuIcon04_Friends", "Friends", () => Open("friends")),
                ("Icon_MenuIcon04_Inbox", "Inbox", () => Open("inbox")),
                ("Icon_MenuIcon04_News", "News", () => Open("news")),
                ("Icon_MenuIcon03_Ornament_n", "Customize", () => Open("customize")),
            };
            for (var i = 0; i < tiles.Length; i++)
            {
                int col = i % cols, row = i / cols;
                var t = PK.Abs(PK.Col(root), right: xR + (cols - 1 - col) * (tileS + gap), top: gridTop + row * (tileS + gap), width: tileS, height: tileS);
                PK.Centre(t);
                HomeNine(t, p, "Button_Square06_Navy", 24, 24, 30, 24, 14);
                PK.Img(t, tiles[i].icon, P(64)).style.marginTop = -P(6);
                HomeTS(t, p, tiles[i].label, 19, 3.5f).style.marginTop = P(4);
                PK.Tap(t, p, tiles[i].go);
                if (newPlayer) continue;
                if (i == 5 && NewsUnread > 0)
                    PK.Abs(PK.Badge(t, p, NewsUnread.ToString(), false, 30), right: -P(8), top: -P(8));
                if (i == 1 && HomeDailyReady)
                    PK.Abs(PK.Badge(t, p, "1", true, 30), right: -P(8), top: -P(8));
                if (i == 2 && spinsLeft > 0)
                {
                    var w = PK.Abs(PK.Centre(PK.Box(t)), right: -P(8), top: -P(8));
                    w.style.backgroundColor = GameTheme.C("#e8253f");
                    Kit.Border(w, p.U(2), Color.white);
                    Kit.Round(w, P(30));
                    PK.Pad(w, P(7), P(3), P(7), P(3));
                    HomeTS(w, p, $"{spinsLeft}/{CpWheelSpinsADay}", 16, 0);
                }
            }

            // ---------------------------------------------------------------- island status chip
            var chip = PK.Row(root, P(10));
            chip.style.position = Position.Absolute;
            chip.style.left = new Length(50, LengthUnit.Percent);
            chip.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            chip.style.bottom = B + P(150);
            chip.style.height = P(48);
            PK.Pad(chip, P(6), 0, P(14), 0);
            chip.style.backgroundColor = GameTheme.T("#061a40", .82f);
            Kit.Border(chip, p.U(3), PK.Ink);
            Kit.Round(chip, P(24));
            var seasonImg = PK.Img(chip, "G_Autumn", P(38));
            var seasonName = HomeTS(chip, p, "", 20, 3);
            var seasonLeft = HomeTS(chip, p, "", 20, 3, GameTheme.C("#ffb15c"));
            seasonLeft.style.marginLeft = P(5);
            HomeTS(chip, p, "|", 20, 3).style.opacity = .4f;
            var weatherImg = PK.Img(chip, "G_Sunny", P(34));
            var weatherName = HomeTS(chip, p, "", 20, 3);
            HomeTS(chip, p, "|", 20, 3).style.opacity = .4f;
            PK.Img(chip, "ResourceBar_Icon_Gold", P(34));
            var worth = HomeTS(chip, p, "", 20, 3);
            string lastSeason = "", lastWeather = "";
            c.Live(() =>
            {
                var e = Economy;
                var now = World.Island.IslandEconomyHost.Clock;
                var season = e != null ? e.SeasonNow(now) : GameplayRules.SeasonAt(now);
                if (season.Name != lastSeason)
                {
                    lastSeason = season.Name;
                    Kit.Picture(seasonImg, GameArt.Get(HomeSeasonArt(season.Name)));
                }

                seasonName.text = Loc.Of(season.Name);
                seasonLeft.text = Loc.Of(HomeClock(GameplayRules.SeasonLeft(now)));
                var w = GameplayRules.WeatherAt(now);
                if (w.ToString() != lastWeather)
                {
                    lastWeather = w.ToString();
                    Kit.Picture(weatherImg, GameArt.Get(HomeWeatherArt(w)));
                    weatherName.text = Loc.Of(w == GameplayRules.WeatherKind.Clear ? "Sunny" : w.ToString());
                }

                worth.text = Loc.Of(PK.Num(e?.NetWorth ?? 0));
            });

            // ---------------------------------------------------------------- bottom tabs
            float tabW = P(132), tabH = P(120), tabGap = P(12), bottom = B + P(10);
            var tabs = new (string art, string icon, string label, Action go)[]
            {
                ("Button03_Purple", "Icon_MenuIcon02_Shop", "SHOP", () => Open("shop")),
                ("Button03_Blue", "Icon_MenuIcon02_Inventory", "STORAGE", () => Open("storage")),
                ("Button03_Blue", "Icon_ImageIcon_Map_l", "ORDERS", () => Open("orders", "orders")),
            };
            for (var i = 0; i < tabs.Length; i++)
            {
                var t = PK.Abs(PK.Col(root), left: xL + i * (tabW + tabGap), bottom: bottom, width: tabW, height: tabH);
                t.style.alignItems = Align.Center;
                t.style.justifyContent = Justify.FlexEnd;
                t.style.paddingBottom = P(14);
                HomeNine(t, p, tabs[i].art, 30, 26, 40, 26, 16);
                var ic = PK.Img(t, tabs[i].icon, P(84));
                ic.style.position = Position.Absolute;
                ic.style.top = -P(14);
                ic.style.left = (tabW - ic.style.width.value.value) / 2f;
                HomeTS(t, p, tabs[i].label, 24, 4);
                PK.Tap(t, p, tabs[i].go);
            }

            // ---------------------------------------------------------------- big buttons
            float bigW = P(290), bigH = P(130);
            var coupons = PK.Abs(PK.Row(root, P(14)), right: xR + bigW + P(14), bottom: bottom, width: bigW, height: bigH);
            coupons.style.justifyContent = Justify.Center;
            Kit.Nine(coupons, GameArt.Get("Button02_Blue"), 38, 120, 38, 120, bigH / 2f / 120f);
            HomeCouponIcon(coupons, p, P(110));
            HomeTS(coupons, p, "COUPONS", 36, 5);
            PK.Tap(coupons, p, () => Open("coupons"));

            var build = PK.Abs(PK.Row(root, P(14)), right: xR, bottom: bottom, width: bigW, height: bigH);
            build.style.justifyContent = Justify.Center;
            Kit.Nine(build, GameArt.Get("Button02_Yellow"), 38, 120, 38, 120, bigH / 2f / 120f);
            PK.Img(build, "Icon_Hammer", P(92)).style.marginLeft = -P(10);
            HomeTS(build, p, "BUILD", 40, 5);
            PK.Tap(build, p, () =>
            {
                if (PlayerPrefs.GetInt(HomeTutorialPref, 0) < 2)
                {
                    PlayerPrefs.SetInt(HomeTutorialPref, 2);
                    PlayerPrefs.Save();
                }

                Go("play", "build");
            });

            // ---------------------------------------------------------------- new player
            if (tut == 0) HomeTalk(c, xR, B, L);
            else if (tut == 1)
            {
                var ring = PK.Img(root, "Icon_ImageIcon_Tutorial_LineCircle", float.NaN, P(220));
                var rt = GameArt.Get("Icon_ImageIcon_Tutorial_LineCircle");
                var rh = rt != null ? P(220) * rt.height / rt.width : P(220);
                ring.style.height = rh;
                PK.Abs(ring, right: xR + bigW / 2f - P(110), bottom: bottom + bigH / 2f - P(110));
                HomePulse(c, ring, 1.08f, 1.2f);
                var ht = GameArt.Get("Icon_ImageIcon_Tutorial_Hand");
                var hand = PK.Box(root);
                PK.Size(hand, P(190), ht != null ? P(190) * ht.height / ht.width : P(190));
                Kit.Picture(hand, ht);
                // the fingertip (94% across, 72% down the art) on the ring's centre, the glove up and to its left
                var handW = P(190);
                var handH = ht != null ? handW * ht.height / ht.width : handW;
                PK.Abs(hand, right: xR + bigW / 2f - handW * (1f - .94f), bottom: bottom + bigH / 2f - handH * (1f - .72f));
                c.Live(() =>
                {
                    var t = (1f - Mathf.Cos(Time.unscaledTime / 1.2f * Mathf.PI * 2f)) / 2f * P(6);
                    hand.style.translate = new Translate(-t, -t);
                });
            }
        }

        /// <summary>The coupon icon: the pack's gold ticket with a stroked "%" (kit.js couponIcon).</summary>
        private static VisualElement HomeCouponIcon(VisualElement parent, Pt p, float w)
        {
            var h = w * 242f / 399f;
            var e = PK.Centre(PK.Box(parent));
            PK.Size(e, w, h);
            Kit.Picture(e, GameArt.Get("Icon_ImageIcon_Pass_Golden"));
            var l = PK.T(e, p, "%", 1);
            l.style.fontSize = h * .62f;
            l.style.unityTextOutlineWidth = Mathf.Max(2f * p.Unit, h * .09f);
            l.style.unityTextOutlineColor = GameTheme.C("#b25b00");
            l.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 0, color = Color.clear };
            l.style.marginTop = -h * .06f;
            return e;
        }

        /// <summary>Grandpa Joe's three lines over a dim screen (tap the bubble for the next, SKIP to go to the hand).</summary>
        private void HomeTalk(Ctx c, float xR, float B, float L)
        {
            var p = c.P;
            float P(float n) => p.P(n);
            var dim = HomeDim(c.Root, "#030c20", .62f);
            dim.pickingMode = PickingMode.Position;   // the rest stays dimmed and untouchable while he talks

            var npc = PK.Abs(PK.Box(c.Root), left: L + P(10), bottom: B, width: P(400), height: P(460));
            Kit.Picture(npc, GameArt.Get("Home_Npc"));
            var name = HomeTS(c.Root, p, "GRANDPA JOE", 44, 6);
            PK.Abs(name, left: L + P(440), bottom: B + P(300));

            void Next(bool skip)
            {
                _homeLine++;
                if (skip || _homeLine >= HomeLines.Length)
                {
                    _homeLine = 0;
                    PlayerPrefs.SetInt(HomeTutorialPref, 1);
                    PlayerPrefs.Save();
                }

                MarkDirty();
            }

            var skipBtn = PK.Centre(PK.Box(c.Root));
            PK.Abs(skipBtn, right: xR + P(30), bottom: B + P(300));
            skipBtn.style.backgroundColor = PK.Ink;
            Kit.Round(skipBtn, p.U(10));
            PK.Pad(skipBtn, P(22), P(12), P(22), P(12));
            HomeTS(skipBtn, p, "SKIP", 26, 0);
            PK.Tap(skipBtn, p, () => Next(true));

            var speech = PK.Abs(PK.Box(c.Root), left: L + P(400), right: xR, bottom: B + P(30), height: P(250));
            HomeNine(speech, p, "BubbleFrame02_White_Bg_Left", 40, 30, 40, 46, 22);
            PK.Pad(speech, P(76), P(46), P(90), P(30));
            var txt = PK.T(speech, p, HomeLines[Mathf.Clamp(_homeLine, 0, HomeLines.Length - 1)], 40, GameTheme.T("#12357a"), stroke: false);
            txt.enableRichText = true;
            txt.style.whiteSpace = WhiteSpace.Normal;
            txt.style.unityTextAlign = TextAnchor.UpperLeft;
            txt.style.flexShrink = 1;
            var nx = PK.Abs(PK.Box(speech), right: P(40), bottom: P(34), width: P(30), height: P(40));
            nx.style.backgroundImage = Background.FromTexture2D(HomeTri());
            nx.style.unityBackgroundImageTintColor = GameTheme.T("#1a8de8");
            c.Live(() => nx.style.translate = new Translate((1f - Mathf.Cos(Time.unscaledTime / .8f * Mathf.PI * 2f)) / 2f * P(5), 0));
            PK.Tap(speech, p, () => Next(false));
        }
    }
}
