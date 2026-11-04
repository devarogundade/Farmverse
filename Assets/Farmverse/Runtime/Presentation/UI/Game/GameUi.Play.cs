#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The island while playing (gameplay.html): the HUD (level + XP, the goal, season · weather · raid chip,
    /// coins, gems, Home; the joystick under the right thumb, STOP while riding; Build · Storage · Orders · Ride
    /// under the left thumb), the labels over the world, and whatever has the player's attention — the build
    /// menu, placing, a building's band, the seed strip, a conversation, the farmhouse rooms, the raid banner.
    /// The screen is rebuilt when what it shows changes (<see cref="PlayKey"/>); numbers update every frame.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegPlay_Play = Register("play", UiLayer.Base, (ui, c) => ui.PlayBase(c), .55f);

        private bool _playBuildMenu;
        private string _playBuildCat = "farm";
        private int _playLastFrame = -10;
        private int _playLevelSeen;
        private int _playRaidsSeen = -1;
        private string _playToastSeen = "";
        private (IslandStation station, int site)? _playDismissed;

        // ------------------------------------------------------------------ what to show

        private string PlayKey()
        {
            var s = Sandbox;
            if (s == null || !s.Running || Economy == null || Host == null) return "off";
            var eco = Economy;
            var host = Host;
            var k = new StringBuilder(64);
            k.Append(PlayView()).Append('|');
            k.Append(s.Riding ? 'R' : '-');
            var life = Life;
            k.Append(life != null && life.RaidUnderway ? (life.RaidLanded ? 'L' : 'r') : '-');
            k.Append(eco.Basket.IsFull ? 'F' : '-');
            k.Append(eco.CurrentGoal?.Id ?? "").Append(s.FollowingTrain ? 'T' : '-');
            return k.ToString();
        }

        /// <summary>The band or panel showing (one at a time), with what decides its layout.</summary>
        private string PlayView()
        {
            var s = Sandbox!;
            var eco = Economy!;
            var host = Host!;
            if (s.Inside) return $"room:{s.InsideUid}:{s.RoomShown}";
            if (s.Placer != null && s.Placer.IsPlacing) return $"place:{s.Placer.Current?.id}:{s.Placer.Moving}";
            if (_playBuildMenu) return "build:" + _playBuildCat;
            if (s.Selected is { } b)
            {
                eco.Departments.TryGetValue(b.uid, out var d);
                eco.Machines.TryGetValue(b.uid, out var m);
                eco.Animals.TryGetValue(b.uid, out var a);
                return $"b:{b.uid}:{b.alt}:{eco.IsBuilt(b.uid)}:{eco.IsRuined(b.uid)}:{d?.Level}:{m?.Level}:{a?.Level}:{s.CanRestyle}:{eco.SupplyOrders.ContainsKey(b.uid)}";
            }

            if (s.SelectedProp is { } prop) return "prop:" + prop.index;
            if (Life is { Talking: true } life) return $"talk:{life.TalkName}:{life.TalkQuestions.Count}";
            var site = host.StationSite;
            if (_playDismissed is { } gone)
            {
                if (gone.station == host.Station && gone.site == site) return "";
                _playDismissed = null;
            }

            // a building's panel only opens once the farmer has stopped on it, not while walking through
            if (host.Station != IslandStation.None && !PlaySettled()) return "";

            switch (host.Station)
            {
                case IslandStation.Plant:
                    return "plant:" + site;
                case IslandStation.Machine when eco.Machines.TryGetValue(site, out var m):
                    return $"machine:{site}:{m.Level}";
                case IslandStation.Collect or IslandStation.Feed when eco.Animals.TryGetValue(site, out var a):
                    return $"animals:{site}:{a.Level}";
                case IslandStation.Unload when eco.Departments.TryGetValue(site, out var shop) && shop.Levels.Kind == DepartmentKind.Market:
                    return $"market:{site}:{string.Join(",", eco.Basket.Stacks.Select(x => x.Item).Distinct())}";
                case IslandStation.Unload when eco.Departments.TryGetValue(site, out var d):
                    return $"store:{site}:{d.Level}:{string.Join(",", PlayStoreGoods(site).Select(g => g.Key))}";
            }

            return "";
        }

        private Vector3 _playLastPos;
        private float _playStillSince;
        private int _playStillFrame = -1;

        /// <summary>How long the farmer must stand still before a building's panel opens.</summary>
        private const float PlaySettleSeconds = .45f;

        /// <summary>The farmer has stood still (riding counts as moving) for a moment.</summary>
        private bool PlaySettled()
        {
            var s = Sandbox;
            var farmer = s != null ? s.Farmer : null;
            if (farmer == null) return true;
            if (_playStillFrame != Time.frameCount)
            {
                _playStillFrame = Time.frameCount;
                var pos = farmer.position;
                var moved = (pos - _playLastPos).sqrMagnitude > .0004f || s!.Riding;   // more than 2 cm this frame
                _playLastPos = pos;
                if (moved) _playStillSince = Time.unscaledTime;
            }

            return Time.unscaledTime - _playStillSince >= PlaySettleSeconds;
        }

        private void PlayDismissStation()
        {
            if (Host == null) return;
            _playDismissed = (Host.Station, Host.StationSite);
            _playFullUid = -1;
            MarkDirty();
        }

        // ------------------------------------------------------------------ the screen

        private void PlayBase(Ctx c)
        {
            var p = c.P;
            var s = Sandbox;
            var fresh = Time.frameCount - _playLastFrame > 3;   // just came here (not a rebuild while playing)
            c.Live(() => _playLastFrame = Time.frameCount);
            if (fresh)
            {
                _playBuildMenu = c.Args is string a && a == "build";
                _playDismissed = null;
            }

            if (s == null)
            {
                PK.T(PK.Centre(PK.Inset(PK.Box(c.Root))), p, "No island in this scene", 30);
                return;
            }

            s.Lobby = false;
            c.Watch(PlayKey);
            PlayEvents(c);
            if (!s.Running || Economy == null || Host == null)
            {
                PlayTopHud(c, true);
                return;
            }

            var view = PlayView();
            var riding = s.Riding;
            var life = Life;
            var raid = life != null && life.RaidUnderway && !s.Inside;
            // a building tapped or stood at shows a small card above it and the game goes on (stick, buttons);
            // only "More" opens the full panel, which pauses the stick
            var cardUid = PlayCardUid(view);
            if (cardUid != _playFullUid) _playFullUid = -1;
            var card = cardUid >= 0 && _playFullUid != cardUid;
            if (cardUid < 0) _playCardShown = -1;
            var bottom = view.Length == 0 || card;

            PlayWorld(c, !s.Inside);
            PlayTopHud(c, false);
            if (bottom) PlayStick(c, riding);
            if (bottom && !riding) PlayButtons(c);

            if (card) PlayCard(c, cardUid, view.StartsWith("b:"));
            else if (view.StartsWith("room:")) PlayRoom(c);
            else if (view.StartsWith("place:")) PlayPlace(c);
            else if (view.StartsWith("build:")) PlayBuildMenu(c);
            else if (view.StartsWith("b:")) PlayBuilding(c, s.Selected!);
            else if (view.StartsWith("prop:")) PlayProp(c, s.SelectedProp!);
            else if (view.StartsWith("talk:")) PlayTalk(c);
            else if (view.StartsWith("plant:")) PlayPlant(c);
            else if (view.StartsWith("machine:")) PlayMachine(c, Host.StationSite);
            else if (view.StartsWith("animals:")) PlayAnimals(c, Host.StationSite);
            else if (view.StartsWith("market:")) PlayMarket(c, Host.StationSite);
            else if (view.StartsWith("store:")) PlayStore(c, Host.StationSite);

            if (bottom && raid) PlayRaid(c);
            else if (bottom && riding) PlayRideChip(c);
            else if (bottom && Economy.Basket.IsFull) PlayBasketFull(c);
        }

        /// <summary>Moments that open a popup: a level reached, the away report, a raid's end; the economy's toasts.</summary>
        private void PlayEvents(Ctx c)
        {
            c.Live(() =>
            {
                var eco = Economy;
                var host = Host;
                if (eco == null || host == null || Sandbox == null || !Sandbox.Running) return;
                if (_playLevelSeen == 0) _playLevelSeen = eco.BestLevel;
                if (eco.BestLevel > _playLevelSeen)
                {
                    _playLevelSeen = eco.BestLevel;
                    var coins = eco.TakeLevelCoins();
                    IslandAudio.Play("level_up", null, 0.9f, 0f, 1f);
                    if (Has("levelup")) Open("levelup", _playLevelSeen);
                    else Say($"Level {_playLevelSeen}!" + (coins > 0 ? $" +{coins} coins" : ""), 3f);
                }

                if (host.PendingReport != null && TopPopup != "welcome" && TopPopup == null) Open("welcome");
                ChapterTick();
                eco.WorkPass = CpPass.Active;   // the Farm Pass's 24-hour offline shift (the server checks it too)

                var life = Life;
                if (life != null)
                {
                    if (_playRaidsSeen < 0) _playRaidsSeen = life.RaidsEnded;
                    if (life.RaidsEnded > _playRaidsSeen)
                    {
                        _playRaidsSeen = life.RaidsEnded;
                        Open("raidresult");
                    }
                }

                var toast = host.ToastNow ?? "";
                if (toast != _playToastSeen)
                {
                    _playToastSeen = toast;
                    if (toast.Length > 0) Say(toast, 2.4f);
                }
            });
        }

        // ------------------------------------------------------------------ top of the HUD

        private void PlayTopHud(Ctx c, bool waiting)
        {
            var p = c.P;
            var eco = Economy;
            var L = p.L + p.P(16);
            var R = p.R + p.P(16);

            // level + XP (tap: profile)
            var lvl = PK.Row(c.Root);
            PK.Abs(lvl, left: L, top: p.P(14));
            var badge = PK.Centre(PK.Box(lvl));
            PK.Size(badge, p.P(74), p.P(82));
            Kit.Contain(badge, GameArt.Get("Slider_Level01_Badge_Blue"));
            var lvlText = PK.T(badge, p, "1", 34);
            var (bar, fill, xp) = PK.Bar(lvl, p, 220, 30, 0, "", true);
            bar.style.marginLeft = p.P(74 - 6);   // the badge sits over the bar's left end
            badge.style.position = Position.Absolute;
            badge.style.left = 0;
            badge.style.top = 0;
            badge.BringToFront();
            lvl.style.height = p.P(82);
            PK.Tap(lvl, p, () => Open("profile"));
            c.Live(() =>
            {
                var e = Economy;
                if (e == null) return;
                var level = e.BestLevel;
                var (from, to) = e.LevelSpan(level);
                lvlText.text = Loc.Of(level.ToString());
                var have = System.Math.Max(0L, e.Xp - from);
                var need = System.Math.Max(1L, to - from);
                PK.SetPct(fill, have * 100f / (float)need);
                xp.text = Loc.Of($"{have}/{need}");
            });

            if (waiting || eco == null) return;

            // the goal (tap: claim when done, else the goals page)
            var goal = PK.Row(c.Root);
            PK.Abs(goal, left: L, top: p.P(110));
            goal.style.backgroundColor = GameTheme.T("#061a40", .8f);
            Kit.Border(goal, p.U(3), PK.Ink);
            Kit.Round(goal, p.P(14));
            PK.Pad(goal, p.P(8), p.P(8), p.P(14), p.P(8));
            PK.Img(goal, "Icon_Quest", p.P(54));
            var gcol = PK.Col(goal, p.P(6));
            gcol.style.marginLeft = p.P(10);
            var gText = PK.T(gcol, p, "", 20);
            var (_, gFill, gCount) = PK.Bar(gcol, p, 170, 22, 0, "");
            PK.Tap(goal, p, PlayGoalTapped);
            c.Live(() =>
            {
                var e = Economy;
                var g = e?.CurrentGoal;
                goal.style.display = g == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (e == null || g == null) return;
                var have = Mathf.Min(e.GoalHave(g), g.Need);
                var done = e.GoalDone(g);
                gText.text = Loc.Of(done ? "Goal done! Tap to claim" : g.Text);
                PK.SetPct(gFill, have * 100f / Mathf.Max(1, g.Need));
                gCount.text = Loc.Of($"{have} / {g.Need}");
            });

            // season · time · weather · raid
            var chip = PlayTopCentre(PlayChip(c.Root, p, 52, 6, 16), p.P(14));
            var seasonArt = PK.Img(chip, "G_Autumn", p.P(40));
            var seasonText = PK.T(chip, p, "", 20);
            var seasonLeft = PK.T(chip, p, "", 20, GameTheme.C("#ffb15c"));
            var sep1 = PK.N(chip, p, "|", 20, GameTheme.Light(.4f));
            var weatherArt = PK.Img(chip, "G_Sunny", p.P(36));
            var weatherText = PK.T(chip, p, "", 20);
            var sep2 = PK.N(chip, p, "|", 20, GameTheme.Light(.4f));
            var raidArt = PK.Img(chip, "Icon_Shield", p.P(36));
            var raidText = PK.T(chip, p, "", 20, GameTheme.C("#7dff6a"));
            // the month's live event, while it runs
            var sep3 = PK.N(chip, p, "|", 20, GameTheme.Light(.4f));
            var eventArt = PK.Img(chip, "Icon_Gift", p.P(36));
            var eventText = PK.T(chip, p, "", 20, GameTheme.C("#ffd84a"));
            foreach (var e in new VisualElement[] { seasonText, seasonLeft, sep1, weatherArt, weatherText, sep2, raidArt, raidText, sep3, eventArt, eventText }) e.style.marginLeft = p.P(10);
            PK.Tap(chip, p, PlayExplainChip);
            // the Winter Festival: Santa's gift of the day, under the chip
            var giftChip = PlayTopCentre(PlayChip(c.Root, p, 52, 14, 18), p.P(80));
            PK.Img(giftChip, "Icon_Gift", p.P(38));
            PK.T(giftChip, p, "Santa's gift: tap to open", 20, GameTheme.C("#ffd84a")).style.marginLeft = p.P(8);
            PK.Tap(giftChip, p, () =>
            {
                var n = IslandFestival.ClaimGift();
                if (n > 0)
                {
                    Economy?.AddCoins(n);
                    IslandAudio.Play("coin");
                    Say($"Santa gave you {PK.Num(n)} coins!", 2.5f);
                }
                MarkDirty();
            });
            giftChip.style.display = DisplayStyle.None;
            c.Live(() => giftChip.style.display = IslandFestival.GiftReady ? DisplayStyle.Flex : DisplayStyle.None);
            string? shownSeason = null, shownWeather = null;
            c.Live(() =>
            {
                var e = Economy;
                if (e == null) return;
                var now = IslandEconomyHost.Clock;
                var season = e.SeasonNow(now);
                if (shownSeason != season.Name)
                {
                    shownSeason = season.Name;
                    Kit.Picture(seasonArt, GameArt.Get(PlaySeasonArt(season.Name)));
                }

                seasonText.text = Loc.Of(season.Name);
                seasonLeft.text = Loc.Of(PlayClock(GameplayRules.SeasonLeft(now)));
                var (wArt, wName) = PlayWeather(GameplayRules.WeatherAt(now));
                if (shownWeather != wArt)
                {
                    shownWeather = wArt;
                    Kit.Picture(weatherArt, GameArt.Get(wArt));
                }

                weatherText.text = Loc.Of(wName);
                var live = e.EventNow(now);
                sep3.style.display = eventArt.style.display = eventText.style.display = live != null && !p.Phone ? DisplayStyle.Flex : DisplayStyle.None;
                if (live != null) eventText.text = Loc.Of(live.Name) + " " + PlayClock(GameplayRules.EventLeft(now));
                var life = Life;
                var raidOn = life != null && life.RaidUnderway;
                var soon = life != null && life.NextRaidIn > 0 && life.NextRaidIn < 600;
                var show = raidOn || soon;
                sep2.style.display = raidArt.style.display = raidText.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) return;
                var safe = e.Defence >= e.Threat;
                raidText.text = Loc.Of(raidOn ? "RAID!" : PlayClock(life!.NextRaidIn));
                raidText.style.color = raidOn || !safe ? GameTheme.C("#ff7a8c") : GameTheme.C("#7dff6a");
            });

            // coins · gems · home
            var right = PK.Row(c.Root);
            PK.Abs(right, top: p.P(14), right: R);
            var (coinPill, coins) = PlayPill(right, p, false, () => Open("shop", "coins"));
            var (gemPill, gems) = PlayPill(right, p, true, () => Open("shop", "gems"));
            gemPill.style.marginLeft = p.P(14);
            var home = PK.Centre(PK.Box(right));
            PK.Size(home, p.P(62), p.P(66));
            home.style.marginLeft = p.P(20);
            Kit.Nine(home, GameArt.Get("Button_Square03_Navy"), 20, 20, 20, 24, p.P(12) / 20f);
            PK.Img(home, "Icon_PictoIcon_Home", p.P(66) * .56f);
            PK.Tap(home, p, () => { WorkLeavingHint(); Go("home"); }, "whoosh");
            c.Live(() =>
            {
                coins.text = Loc.Of(PK.Num(Sandbox != null ? Sandbox.CoinsNow : 0));
                gems.text = Loc.Of(PK.Num(Wallet.Gems));
            });
        }

        /// <summary>The camera distance at which world labels are drawn at their design size.</summary>
        private const float PlayLabelNear = 70f;

        /// <summary>World labels' size at that distance, as a share of the design size (the design's read too big over the 3D island).</summary>
        private const float PlayLabelSize = .55f;

        /// <summary>
        /// How big a world label is drawn: its design size at the camera's normal distance, smaller as the camera
        /// zooms out (a little bigger zoomed in), and 0 (hidden) once it would be under half size.
        /// </summary>
        private static float PlayLabelScale(Camera cam, Vector3 at)
        {
            var d = Vector3.Distance(cam.transform.position, at);
            if (d <= .01f) return 1f;
            var k = PlayLabelNear / d;
            return k < .5f ? 0f : Mathf.Min(k, 1.15f) * PlayLabelSize;
        }

        /// <summary>Scales a label about its anchor (bottom centre for tags, centre for bars; y in percent).</summary>
        private static void PlayScaleAt(VisualElement e, float k, float originY)
        {
            e.style.transformOrigin = new TransformOrigin(new Length(50, LengthUnit.Percent), new Length(originY, LengthUnit.Percent));
            e.style.scale = new Scale(new Vector3(k, k, 1f));
        }

        private void PlayGoalTapped()
        {
            var eco = Economy;
            if (eco?.CurrentGoal is not { } goal) return;
            if (!eco.GoalDone(goal))
            {
                Open("orders", "goals");
                return;
            }

            if (!eco.ClaimGoal()) return;
            IslandAudio.Play("cheer", null, 0.7f, 0f, 1f);
            Say($"Goal complete! +{PK.Num(goal.Reward)} coins", 3f);
        }

        private void PlayExplainChip()
        {
            var eco = Economy;
            if (eco == null) return;
            var now = IslandEconomyHost.Clock;
            var season = eco.SeasonNow(now);
            var life = Life;
            var raid = life != null && life.RaidUnderway ? "<br><br><b>Zombies are on the island!</b>" :
                life != null && life.NextRaidIn > 0 ? $"<br><br>Next raid in about <b>{PlayMinutes(life.NextRaidIn)}</b><br>Defence {eco.Defence:0.#} vs threat {eco.Threat:0.#}" : "";
            var live = eco.EventNow(now);
            var ev = live != null ? $"<br><br><b>{live.Name}</b> ({PlayClock(GameplayRules.EventLeft(now))} left): {live.Hint}."
                : $"<br><br>Next event: <b>{GameplayRules.ThemeOf(now).Name}</b> in {PlayClock(GameplayRules.EventLeft(now))}.";
            Tell(season.Name, $"{season.Hint} ({PlayClock(GameplayRules.SeasonLeft(now))} left).<br><br>{GameplayRules.WeatherText(GameplayRules.WeatherAt(now))}.{ev}{raid}");
        }

        // ------------------------------------------------------------------ bottom of the HUD

        private void PlayButtons(Ctx c)
        {
            var p = c.P;
            var L = p.L + p.P(16);
            var B = p.B + p.P(16);
            var eco = Economy!;
            PlaySkill(c.Root, p, "Icon_Hammer", "Build", L, B + p.P(10), 150, () =>
            {
                _playBuildMenu = true;
                MarkDirty();
            });
            var storage = PlaySkill(c.Root, p, "Icon_MenuIcon02_Inventory", "Storage", L + p.P(170), B, 118, () => Open("storage"), "!");
            var orders = PlaySkill(c.Root, p, "Icon_ImageIcon_Map_l", "Orders", L, B + p.P(190), 118, () => Open("orders"), "0");
            // Ride is a toggle: on = on the horse (the stick steers it and sets the pace), off = on foot
            var ride = PlaySkill(c.Root, p, "Icon_Horsesheos", "Ride", L + p.P(150), B + p.P(160), 104, () => { Sandbox?.ToggleRide(); MarkDirty(); });
            var rideOn = PK.Inset(PK.Box(ride), -p.P(6), -p.P(6), -p.P(6), -p.P(6));
            Kit.Round(rideOn, p.P(120));
            Kit.Border(rideOn, p.U(6), GameTheme.C("#ffd84a"));
            rideOn.pickingMode = PickingMode.Ignore;
            var rideLabel = ride.Q<Label>();
            c.Live(() =>
            {
                var on = Sandbox?.Riding ?? false;
                rideOn.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (rideLabel != null) rideLabel.text = Loc.Of(on ? "Get off" : "Ride");
            });
            // the freight train (from level 5): a badge while it waits at the station
            var train = eco.BestLevel >= GameplayRules.TrainLevel ? PlaySkill(c.Root, p, "Icon_Chest", "Truck", L + p.P(300), B, 118, () => Open("train"), "!") : null;
            var trainBadge = train?.Children().Last();
            var storageBadge = storage.Children().Last();
            var ordersBadge = orders.Children().Last();
            var ordersText = ordersBadge.Q<Label>();
            c.Live(() =>
            {
                var e = Economy;
                if (e == null) return;
                storageBadge.style.display = e.Basket.IsFull ? DisplayStyle.Flex : DisplayStyle.None;
                if (trainBadge != null) trainBadge.style.display = e.Train != null && !e.Train.AllLoaded ? DisplayStyle.Flex : DisplayStyle.None;
                var ready = e.Dailies.Count(i => !e.DailyClaimed(i) && e.DailyHave(i) >= e.DailyNeed(i))
                            + (e.CurrentGoal is { } g && e.GoalDone(g) ? 1 : 0);
                ordersBadge.style.display = ready > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (ordersText != null) ordersText.text = Loc.Of(ready.ToString());
            });
        }

        /// <summary>
        /// The joystick under the right thumb (the pack's Play_Joystick), or the see-through red STOP while
        /// riding. The farmer's stick (VirtualJoystick) is placed on it and reads presses there itself.
        /// </summary>
        private void PlayStick(Ctx c, bool riding)
        {
            var p = c.P;
            var size = p.P(230);
            var stick = PlayAt(c.Root, p.R + p.P(16), p.B + p.P(16), size, size, true);
            var root = Root2Screen(c.Root);
            VirtualJoystick.CentreOverride = () =>
            {
                var b = stick.worldBound;
                var k = root();
                return new Vector2(b.center.x * k, Screen.height - b.center.y * k);
            };
            VirtualJoystick.RadiusOverride = size * .5f * .62f * root();

            if (riding)
            {
                PK.Centre(stick);
                Kit.Round(stick, size);
                Kit.Picture(stick, GameArt.Radial("pstop", .5f, .5f, .5f, .5f, (0f, new Color(1f, .35f, .35f, .22f)), (1f, new Color(.84f, .18f, .23f, .32f))));
                Kit.Border(stick, p.P(5), GameTheme.Light(.45f));
                var inner = PK.Centre(PK.Box(stick));
                PK.Size(inner, p.P(110), p.P(110));
                Kit.Round(inner, p.P(110));
                inner.style.backgroundColor = new Color(.84f, .18f, .23f, .55f);
                Kit.Border(inner, p.P(4), GameTheme.Light(.7f));
                PK.T(inner, p, "STOP", 30);
                return;
            }

            stick.style.opacity = .9f;
            Kit.Picture(stick, GameArt.Get("Play_Joystick_bg"));
            var hw = p.P(90);
            var tex = GameArt.Get("Play_Joystick_handle");
            var hh = tex != null && tex.width > 0 ? hw * tex.height / tex.width : hw;
            var handle = PK.Abs(PK.Box(stick), width: hw, height: hh);
            Kit.Picture(handle, tex);
            c.Live(() =>
            {
                var j = VirtualJoystick.Current;
                var v = j != null && j.Engaged ? j.Value : Vector2.zero;
                var rim = size * .5f * .62f;
                handle.style.left = size / 2f - hw * .3f + v.x * rim;
                handle.style.top = size / 2f - hh * .75f - v.y * rim;
                stick.style.opacity = j != null && j.Engaged ? 1f : .9f;
            });
        }

        /// <summary>Panel pixels to screen pixels (the panel may be scaled).</summary>
        private static System.Func<float> Root2Screen(VisualElement anyInPanel) => () =>
        {
            var top = anyInPanel.panel?.visualTree;
            var w = top != null ? top.layout.width : 0f;
            return w > 1f ? Screen.width / w : 1f;
        };

        // ------------------------------------------------------------------ banners

        private void PlayRaid(Ctx c)
        {
            var p = c.P;
            var life = Life!;
            var banner = PlayTopCentre(PK.Col(c.Root, p.P(4)), p.P(80));
            PK.Size(banner, p.P(780), p.P(150));
            PK.Centre(banner);
            banner.style.paddingBottom = p.P(14);
            Kit.Picture(banner, GameArt.Get("Play_ActionText_Frame_Red"));
            PK.T(banner, p, "RAID!", 46);
            var sub = PK.T(banner, p, "", 22);
            var buttons = PlayTopCentre(PK.Row(c.Root, p.P(16)), p.P(240));
            PlayBtn(buttons, p, "blue", "Icon_Target", "Look", 180, 62, 24, () => Life?.LookAtRaid());
            PlayBtn(buttons, p, "yellow", "Icon_Horsesheos", "Ride there", 230, 62, 24, () => Life?.RideToRaid());
            c.Live(() =>
            {
                var l = Life;
                if (l == null) return;
                var n = l.RaidersLeft;
                sub.text = Loc.Of(l.RaidLanded ? $"{n} zombie{(n == 1 ? "" : "s")} from the {l.RaidFrom}" : $"Zombies from the {l.RaidFrom} · coming in {Mathf.CeilToInt(l.RaidLandsIn)}s");
            });
        }

        private void PlayRideChip(Ctx c)
        {
            var p = c.P;
            var chip = PlayTopCentre(PlayChip(c.Root, p, 52, 18, 18), p.P(80));
            PK.Img(chip, "Icon_Horsesheos", p.P(36));
            PK.T(chip, p, "Riding · farm work paused", 20).style.marginLeft = p.P(8);
        }

        private void PlayBasketFull(Ctx c)
        {
            var p = c.P;
            var pill = PlayTopCentre(PK.Row(c.Root), p.P(80));
            pill.style.backgroundColor = new Color(110 / 255f, 20 / 255f, 30 / 255f, .95f);
            Kit.Border(pill, p.U(3), PK.Ink);
            Kit.Round(pill, p.P(60));
            PK.Pad(pill, p.P(12), p.P(10), p.P(24), p.P(10));
            PK.Img(pill, "Icon_Bag", p.P(46));
            PK.T(pill, p, "Basket full! Take it to the farmhouse.", 24).style.marginLeft = p.P(12);
        }

        // ------------------------------------------------------------------ over the world

        private readonly List<IslandLabel> _playLabels = new List<IslandLabel>();
        private readonly List<(Vector3 at, float health, bool raider)> _playHurt = new List<(Vector3, float, bool)>();

        private sealed class PlayBubble
        {
            public VisualElement Root = null!, Bubble = null!, Icon = null!, Bar = null!, Fill = null!, Tail = null!;
            public Label Text = null!;
            public string Art = "\u0000";
            public IslandLabelKind Kind = (IslandLabelKind)(-1);
        }

        /// <summary>
        /// Bubbles over fields, machines and stores (Host.Labels), the basket count over the farmer, health bars
        /// in a raid and the red arrow at the edge toward raiders off screen. Drawn at the design's world-label
        /// size (the mockup's 1600×1000 view).
        /// </summary>
        private void PlayWorld(Ctx c, bool on)
        {
            var layer = c.World;
            if (layer == null || !on) return;
            var p = c.P;
            var f = Mathf.Max(p.W / 1600f, p.H / 1000f);
            var tags = new List<PlayBubble>();
            var bars = new List<(VisualElement root, VisualElement fill)>();
            var next = 0f;

            var basket = PK.Box(layer);
            basket.style.position = Position.Absolute;
            basket.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-100, LengthUnit.Percent));
            basket.style.backgroundColor = GameTheme.T("#0b1d3a", .8f);
            Kit.Round(basket, 40 * f);
            PK.Pad(basket, 12 * f, 3 * f, 12 * f, 3 * f);
            var basketText = PlayWorldText(basket, "", 24 * f);

            var arrow = PK.Box(layer);
            arrow.style.position = Position.Absolute;
            PK.Size(arrow, p.P(46), p.P(60));
            Kit.Picture(arrow, PlayTriangle(GameTheme.C("#ff2a2a")));

            c.Live(() =>
            {
                var s = Sandbox;
                var host = Host;
                var cam = Camera.main;
                var panel = layer.panel;
                if (s == null || host == null || cam == null || panel == null) return;
                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + .25f;
                    host.Labels(_playLabels);
                }

                Vector2? ToPanel(Vector3 w)
                {
                    var sp = cam.WorldToScreenPoint(w);
                    if (sp.z <= .1f) return null;
                    return RuntimePanelUtils.CameraTransformWorldToPanel(panel, w, cam);
                }

                var farmer = s.Farmer;
                var shown = 0;
                foreach (var l in _playLabels)
                {
                    if (farmer != null && (l.At - farmer.position).sqrMagnitude > 90f * 90f) continue;
                    var k = PlayLabelScale(cam, l.At);
                    if (k <= 0f) continue;   // zoomed too far out: the label stands aside
                    var at = ToPanel(l.At);
                    if (at == null) continue;
                    while (tags.Count <= shown) tags.Add(PlayNewTag(layer, f));
                    var tag = tags[shown++];
                    PlayShowTag(tag, l, at.Value, f);
                    PlayScaleAt(tag.Root, k, 100f);
                }

                for (var i = shown; i < tags.Count; i++) tags[i].Root.style.display = DisplayStyle.None;

                // the basket over the farmer
                var eco = Economy;
                var fp = farmer != null && eco != null && !s.Riding ? ToPanel(farmer.position + Vector3.up * 2.6f) : null;
                var bk = farmer != null ? PlayLabelScale(cam, farmer.position) : 0f;
                basket.style.display = fp != null && bk > 0f && eco!.Basket.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (fp != null)
                {
                    PlayScaleAt(basket, bk, 100f);
                    basket.style.left = fp.Value.x;
                    basket.style.top = fp.Value.y;
                    basketText.text = Loc.Of($"{eco!.Basket.Count} / {eco.Basket.Capacity}");
                }

                // health bars in a raid
                var life = Life;
                _playHurt.Clear();
                life?.Hurt(_playHurt);
                var n = 0;
                foreach (var (w, health, raider) in _playHurt)
                {
                    var hk = PlayLabelScale(cam, w);
                    if (hk <= 0f) continue;
                    var hp = ToPanel(w);
                    if (hp == null) continue;
                    if (n >= bars.Count)
                    {
                        var holder = PK.Box(layer);
                        holder.style.position = Position.Absolute;
                        var fill = PlayThinBar(holder, 100 * f, 14 * f, Color.red, 2 * f);
                        bars.Add((holder, fill));
                    }

                    var (root, fl) = bars[n++];
                    root.style.display = DisplayStyle.Flex;
                    root.style.left = hp.Value.x - 50 * f;
                    root.style.top = hp.Value.y - 7 * f;
                    PlayScaleAt(root, hk, 50f);
                    fl.style.width = new Length(Mathf.Clamp01(health) * 100f, LengthUnit.Percent);
                    fl.style.backgroundColor = raider ? GameTheme.C("#ff3a3a") : GameTheme.C("#7dff6a");
                }

                for (var i = n; i < bars.Count; i++) bars[i].root.style.display = DisplayStyle.None;

                PlayPointAtRaid(arrow, layer, cam, p);
            });
        }

        private static Label PlayWorldText(VisualElement parent, string text, float px)
        {
            var l = new Label(Loc.Of(text)) { pickingMode = PickingMode.Ignore };
            var font = GameArt.Display;
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            l.style.fontSize = px;
            l.style.color = Color.white;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            var sw = Mathf.Max(3, px * .14f);
            l.style.unityTextOutlineWidth = sw;
            l.style.unityTextOutlineColor = PK.Ink;
            l.style.textShadow = new TextShadow { offset = new Vector2(0, sw * .7f), blurRadius = 0, color = PK.Ink };
            parent.Add(l);
            return l;
        }

        /// <summary>A world bubble (the mockup's bubble()): coloured box, ink edge, icon, text, a bar, the tail under it.</summary>
        private static PlayBubble PlayNewTag(VisualElement layer, float f)
        {
            var t = new PlayBubble();
            t.Root = PK.Box(layer);
            t.Root.style.position = Position.Absolute;
            t.Root.style.alignItems = Align.Center;
            t.Root.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-100, LengthUnit.Percent));
            t.Bubble = PK.Row(t.Root);
            Kit.Border(t.Bubble, 4 * f, PK.Ink);
            Kit.Round(t.Bubble, 16 * f);
            PK.Pad(t.Bubble, 14 * f, 6 * f, 14 * f, 6 * f);
            PK.DropEdge(t.Bubble, 4 * f, PK.Ink);
            t.Icon = PK.Box(t.Bubble);
            PK.Size(t.Icon, 40 * f, 40 * f);
            t.Icon.style.marginRight = 8 * f;
            t.Text = PlayWorldText(t.Bubble, "", 28 * f);
            var bar = PK.Box(t.Bubble);
            PK.Size(bar, 70 * f, 14 * f);
            bar.style.marginLeft = 8 * f;
            bar.style.backgroundColor = PK.Ink;
            Kit.Round(bar, 7 * f);
            PK.Clip(bar);
            t.Bar = bar;
            t.Fill = PK.Abs(PK.Box(bar), left: 0, top: 0, bottom: 0);
            t.Fill.style.backgroundColor = GameTheme.C("#ffd23a");
            t.Tail = PK.Box(t.Root);
            PK.Size(t.Tail, 14 * f, 20 * f);
            t.Tail.style.marginTop = -1 * f;
            t.Tail.style.rotate = new Rotate(new Angle(90, AngleUnit.Degree));
            Kit.Picture(t.Tail, PlayTriangle(PK.Ink));
            return t;
        }

        private static void PlayShowTag(PlayBubble t, IslandLabel l, Vector2 at, float f)
        {
            t.Root.style.display = DisplayStyle.Flex;
            t.Root.style.left = at.x;
            t.Root.style.top = at.y;
            if (t.Kind != l.Kind)
            {
                t.Kind = l.Kind;
                var info = l.Kind == IslandLabelKind.Info;
                t.Bubble.style.backgroundColor = l.Kind switch
                {
                    IslandLabelKind.Ready => GameTheme.C("#7ad957"),
                    IslandLabelKind.Alarm => GameTheme.C("#e8253f"),
                    IslandLabelKind.Progress => GameTheme.T("#2a6fd0"),
                    _ => GameTheme.T("#0b1d3a", .75f),
                };
                Kit.Border(t.Bubble, info ? 0 : 4 * f, PK.Ink);
                t.Bubble.style.borderBottomWidth = info ? 0 : 8 * f;
                Kit.Round(t.Bubble, info ? 40 * f : 16 * f);
                t.Tail.style.display = info ? DisplayStyle.None : DisplayStyle.Flex;
                t.Text.style.fontSize = (info ? 22 : l.Kind == IslandLabelKind.Ready ? 30 : 28) * f;
            }

            var art = !string.IsNullOrEmpty(l.Item) ? PlayGood(l.Item!) : l.Kind == IslandLabelKind.Progress ? "Icon_PictoIcon_Timer" : "";
            if (t.Art != art)
            {
                t.Art = art;
                t.Icon.style.display = art.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (art.Length > 0) Kit.Contain(t.Icon, GameArt.Get(art));
            }

            t.Text.text = Loc.Of(l.Text);
            t.Bar.style.display = l.Progress >= 0f ? DisplayStyle.Flex : DisplayStyle.None;
            if (l.Progress >= 0f) t.Fill.style.width = new Length(Mathf.Clamp01(l.Progress) * 100f, LengthUnit.Percent);
            if (l.Health >= 0f && l.Progress < 0f)
            {
                t.Bar.style.display = DisplayStyle.Flex;
                t.Fill.style.width = new Length(Mathf.Clamp01(l.Health) * 100f, LengthUnit.Percent);
                t.Fill.style.backgroundColor = l.Health < .35f ? GameTheme.C("#ff3a3a") : GameTheme.C("#7dff6a");
            }
            else t.Fill.style.backgroundColor = GameTheme.C("#ffd23a");
        }

        /// <summary>The blinking red arrow at the screen's edge, toward raiders out of view.</summary>
        private void PlayPointAtRaid(VisualElement arrow, VisualElement layer, Camera cam, Pt p)
        {
            var life = Life;
            if (life == null || !life.RaidLanded || life.RaidersLeft == 0)
            {
                arrow.style.display = DisplayStyle.None;
                return;
            }

            var sp = cam.WorldToScreenPoint(life.RaidFocus);
            var onScreen = sp.z > 0 && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height;
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var dir = new Vector2(sp.x, sp.y) - centre;
            if (sp.z < 0) dir = -dir;
            if (onScreen || dir.sqrMagnitude < 1f)
            {
                arrow.style.display = DisplayStyle.None;
                return;
            }

            dir.Normalize();
            var size = layer.layout.size;
            var mid = size / 2f;
            var pd = new Vector2(dir.x, -dir.y);
            var at = mid + Vector2.Scale(pd, mid * .86f);
            at.y = Mathf.Clamp(at.y, p.P(300), size.y - p.P(280));
            at.x = Mathf.Clamp(at.x, p.L + p.P(40), size.x - p.R - p.P(40));
            arrow.style.display = DisplayStyle.Flex;
            arrow.style.left = at.x - p.P(23);
            arrow.style.top = at.y - p.P(30);
            arrow.style.rotate = new Rotate(new Angle(Mathf.Atan2(pd.y, pd.x) * Mathf.Rad2Deg, AngleUnit.Degree));
            arrow.style.opacity = .55f + .45f * Mathf.Abs(Mathf.Cos(Time.unscaledTime * Mathf.PI));
        }
    }
}
