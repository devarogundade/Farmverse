#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // gameplay.html's visit states: pick a farm, sailing there, their island (read-only HUD), a building's
    // read-only card and the owner's profile.
    //
    // The farms come from the server: friends (FriendsService) and this week's board (LeaderboardService).
    // Sailing fetches the island as the server last ran it (visits-get); the sandbox then shows its buildings
    // in place of yours (look only) until you head home. Open with ui.Go("visittravel", PlayVisitTarget).
    public sealed partial class GameUi
    {
        private static readonly bool RegPlay_Visit = Register("visit", UiLayer.Base, (ui, c) => ui.PlayVisitBase(c), .55f);
        private static readonly bool RegPlay_Travel = Register("visittravel", UiLayer.Base, (ui, c) => ui.PlayVisitTravel(c), .55f, covers: true);
        private static readonly bool RegPlay_VisitPick = Register("visitpick", UiLayer.Popup, (ui, c) => ui.PlayVisitPick(c), .55f);
        private static readonly bool RegPlay_VisitProfile = Register("visitprofile", UiLayer.Popup, (ui, c) => ui.PlayVisitProfile(c), .55f);

        /// <summary>A farm to visit (from a friend or leaderboard row, or the pick list).</summary>
        public sealed class PlayVisitTarget
        {
            public string Uid = "";
            public bool Loaded, Loading, LikedToday;
            public List<(string id, int x, int z, int w, int d)> Island = new List<(string, int, int, int, int)>();
            public string Name = "Farmer";
            public string Farm = "Farm";
            public int Level = 1;
            public long Worth;
            public string Rank = "—";
            public string Flag = "Eng";
            public string Avatar = "Icon_Egg";
            public int Likes;
            public int Buildings;
            public long Crops;
            public int Raids;
        }

        /// <summary>A farm from a board row.</summary>
        private static PlayVisitTarget PlayVisitFrom(Farmverse.Application.Features.BoardRow r, string rank) => new PlayVisitTarget
        {
            Uid = r.Uid, Name = r.Name, Farm = FarmTitle(r.Worth), Level = r.Level, Worth = r.Worth, Rank = rank,
            Flag = FlagOfIso(r.Locale), Avatar = AvatarArtOf(r.AvatarId),
        };

        /// <summary>Farms to visit for a tab: friends, the week's top farms, or a shuffle of the board.</summary>
        private List<PlayVisitTarget> PlayVisitFarms(string tab)
        {
            var me = Board?.Mine?.Uid;
            var list = new List<PlayVisitTarget>();
            if (tab == "fr")
            {
                var onBoard = Board?.Friends.ToDictionary(f => f.Uid) ?? new Dictionary<string, Farmverse.Application.Features.BoardRow>();
                foreach (var f in Friends?.Friends ?? (IReadOnlyList<Farmverse.Application.Features.FriendRow>)Array.Empty<Farmverse.Application.Features.FriendRow>())
                    list.Add(onBoard.TryGetValue(f.Uid, out var row) ? PlayVisitFrom(row, "—")
                        : new PlayVisitTarget { Uid = f.Uid, Name = f.Name, Farm = f.Name + "'s farm", Avatar = AvatarArtOf(f.AvatarId) });
                return list;
            }
            var world = Board?.World ?? (IReadOnlyList<Farmverse.Application.Features.BoardRow>)Array.Empty<Farmverse.Application.Features.BoardRow>();
            for (var i = 0; i < world.Count; i++)
                if (world[i].Uid != me) list.Add(PlayVisitFrom(world[i], "#" + (i + 1)));
            if (tab == "rand") list = list.OrderBy(x => (x.Uid.GetHashCode() ^ (int)(IslandEconomyHost.Clock / 60)) & 0xffff).ToList();
            return list;
        }

        private string _playVisitTab = "fr";

        private PlayVisitTarget? _playVisitLast;

        private PlayVisitTarget PlayVisitOf(object? args)
        {
            if (args is PlayVisitTarget t) return _playVisitLast = t;
            if (args is string uid)
                return _playVisitLast = PlayVisitFarms("fr").Concat(PlayVisitFarms("top")).FirstOrDefault(x => x.Uid == uid)
                    ?? (_playVisitLast?.Uid == uid ? _playVisitLast : new PlayVisitTarget { Uid = uid });
            return _playVisitLast ?? new PlayVisitTarget();
        }

        /// <summary>Fetch the island (visits-get) while sailing.</summary>
        private async void PlayVisitLoad(PlayVisitTarget owner)
        {
            var visits = Visits;
            if (owner.Loading || owner.Loaded) return;
            if (visits == null || string.IsNullOrEmpty(owner.Uid))
            {
                Say("Connect to the internet to visit farms");
                Go("play");
                return;
            }
            owner.Loading = true;
            var result = await visits.VisitsGet(owner.Uid);
            owner.Loading = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code == Farmverse.Contracts.ErrorCode.VisitsClosed ? owner.Name + "'s farm isn't open to visitors" : "Couldn't reach that farm — check your connection");
                Go("play");
                return;
            }
            var v = result.Value;
            owner.Name = v.Name;
            owner.Farm = FarmTitle(v.Worth);
            owner.Level = (int)v.Level;
            owner.Worth = v.Worth;
            owner.Likes = (int)v.Likes;
            owner.LikedToday = v.LikedToday;
            owner.Crops = v.Crops;
            owner.Raids = (int)v.Raids;
            owner.Flag = FlagOfIso(v.Locale);
            owner.Avatar = AvatarArtOf(v.AvatarId);
            owner.Buildings = v.Buildings.Count;
            owner.Island = v.Buildings.Select(b => (b.Id, b.X, b.Z, b.W, b.D)).ToList();
            owner.Loaded = true;
            MarkDirty();
        }

        /// <summary>Like (visits-like): once a day per farm.</summary>
        private async void PlayVisitLike(PlayVisitTarget owner)
        {
            var visits = Visits;
            if (visits == null) return;
            if (owner.LikedToday) { Say("You already liked this farm today"); return; }
            owner.LikedToday = true;
            MarkDirty();
            var result = await visits.VisitsLike(owner.Uid);
            if (result.IsFailure) { owner.LikedToday = false; Say("Couldn't send the like — check your connection"); MarkDirty(); return; }
            owner.Likes = (int)result.Value.Likes;
            IslandAudio.Play("cheer", null, 0.7f);
            Say($"You liked {owner.Name}'s farm", 2.5f);
            MarkDirty();
        }

        // ------------------------------------------------------------------ their island

        private void PlayVisitBase(Ctx c)
        {
            var p = c.P;
            var s = Sandbox;
            var owner = PlayVisitOf(c.Args);
            c.Live(() => _playLastFrame = Time.frameCount);
            if (s != null)
            {
                s.Lobby = false;
                if (!s.Visiting && owner.Loaded) s.ShowVisit(owner.Island);
            }

            c.Watch(() => s != null && s.Selected != null ? s.Selected.uid : -1);
            c.Watch(() => owner.LikedToday);
            if (s != null && s.Running) PlayWorld(c, true);

            var L = p.L + p.P(16);
            var R = p.R + p.P(16);
            var B = p.B + p.P(16);

            // the yellow-black stripe along the top
            var stripe = PK.Row(c.Root);
            PK.Abs(stripe, left: 0, top: 0, right: 0, height: p.P(8));
            PK.Clip(stripe);
            var cell = p.U(40);
            for (var i = 0; i * cell < p.W + cell; i++)
            {
                var b = PK.Box(stripe);
                PK.Size(b, cell, p.P(8));
                b.style.backgroundColor = i % 2 == 0 ? GameTheme.C("#ffd23a") : PK.Ink;
            }

            // the owner's card
            var card = PK.Row(c.Root);
            PK.Abs(card, left: L, top: p.P(20));
            card.style.backgroundColor = GameTheme.T("#061a40", .88f);
            Kit.Border(card, p.U(3), PK.Ink);
            Kit.Round(card, p.P(18));
            PK.Pad(card, p.P(10), p.P(10), p.P(20), p.P(10));
            PlayAvatar(card, p, owner.Avatar, 86);
            var words = PK.Col(card, p.P(6));
            words.style.marginLeft = p.P(14);
            var nm = PK.Row(words, p.P(8));
            PK.T(nm, p, owner.Name, 28);
            Kit.Round(PK.Img(nm, "Icon_Flag_" + owner.Flag, p.P(24)), p.U(4));
            PK.N(words, p, $"{owner.Farm} · Island Lv {owner.Level}", 15, GameTheme.T("#9fd0ff"));
            var worth = PK.Row(words, p.P(6));
            PK.Img(worth, "ResourceBar_Icon_Gold", p.P(28));
            PK.T(worth, p, PK.Num(owner.Worth), 20);
            PK.N(worth, p, "World " + owner.Rank, 14, GameTheme.C("#ffd23a")).style.marginLeft = p.P(6);

            // VISITING
            var tag = PlayTopCentre(PK.Row(c.Root, p.P(10)), p.P(24));
            tag.style.backgroundColor = new Color(1f, 210 / 255f, 58 / 255f, .95f);
            Kit.Border(tag, p.U(3), PK.Ink);
            Kit.Round(tag, p.P(40));
            PK.Pad(tag, p.P(10), p.P(8), p.P(22), p.P(8));
            var look = PK.Img(tag, "Icon_PictoIcon_Search", p.P(34));
            look.style.unityBackgroundImageTintColor = PK.Ink;
            PK.T(tag, p, "VISITING", 24);

            // back home
            var right = PK.Row(c.Root, p.P(12));
            PK.Abs(right, top: p.P(20), right: R);
            PlayBtn(right, p, "green", "Icon_PictoIcon_Home", "My island", 230, 70, 24, () => { Sandbox?.EndVisit(); Go("play"); });

            var selected = s != null ? s.Selected : null;
            if (selected != null)
            {
                PlayVisitBuilding(c, owner, selected);
                return;
            }

            PlayStick(c, false);
            var heart = (GameTheme.C("#ff6b8a"), GameTheme.C("#c2183c"));
            PlaySkill(c.Root, p, "Icon_Heart", owner.LikedToday ? "Liked!" : "Like", L, B + p.P(10), 150, () => PlayVisitLike(owner), null, heart);
            PlaySkill(c.Root, p, "Icon_PictoIcon_Next01", "Next farm", L + p.P(170), B, 118, () =>
            {
                var farms = PlayVisitFarms("top");
                if (farms.Count == 0) { Say("No other farms on this week's board yet"); return; }
                var i = farms.FindIndex(x => x.Uid == owner.Uid);
                var next = farms[(i + 1 + farms.Count) % farms.Count];
                Sandbox?.EndVisit();
                Go("visittravel", next);
            });
            PlaySkill(c.Root, p, "Icon_MenuIcon04_Friends", "Friends", L, B + p.P(190), 118, () => Open("visitpick", owner));
            PlaySkill(c.Root, p, "Icon_ImageIcon_Info", "Profile", L + p.P(150), B + p.P(160), 104, () => Open("visitprofile", owner));
        }


        /// <summary>A round avatar: blue glow, the pack's profile frame, the picture.</summary>
        private static VisualElement PlayAvatar(VisualElement parent, Pt p, string art, float s)
        {
            var a = PK.Centre(PK.Box(parent));
            PK.Size(a, p.P(s), p.P(s));
            Kit.Round(a, p.P(s));
            Kit.Picture(a, GameArt.Radial("ava_" + GameTheme.Current, .5f, .5f, .5f, .5f, (0f, GameTheme.T("#5fc0ff")), (.999f, GameTheme.T("#1676d1")), (1f, GameTheme.T("#1676d1", 0f))));
            var frame = PK.Inset(PK.Box(a), -p.P(s) * .07f, -p.P(s) * .07f, -p.P(s) * .07f, -p.P(s) * .07f);
            Kit.Picture(frame, GameArt.Get("ProfileFrame01_Border"));
            PK.Fit(a, art, p.P(s) * .62f, p.P(s) * .62f);
            return a;
        }

        /// <summary>A building on their island: look, don't touch, and "Build one too".</summary>
        private void PlayVisitBuilding(Ctx c, PlayVisitTarget owner, PlacedBuilding b)
        {
            var p = c.P;
            var s = Sandbox!;
            var eco = Economy;
            var name = s.SelectedEntry?.displayName ?? IslandEconomyHost.NameOf(b.id);
            var row = PlayBand(c.Root, p, p.Phone ? 300 : 250, () => { s.Deselect(); MarkDirty(); });
            var col = PK.Col(row, p.P(8));
            col.style.width = p.P(260);
            col.style.flexShrink = 0;
            var head = PK.Row(col, p.P(10));
            PK.T(head, p, name, 32);
            if (eco != null && PlayLevelOf(b.uid) > 0) PlayTag(head, p, "Lv " + PlayLevelOf(b.uid), 16, GameTheme.C("#ffd23a"), PK.Ink);
            PK.N(col, p, owner.Name + "'s building", 16, GameTheme.T("#bfe6ff"));
            var line = PK.Row(col, p.P(8));
            PK.Img(line, "Icon_Friends", p.P(26));
            PK.N(line, p, PlayStatus(b.uid), 16, GameTheme.T("#bfe6ff"));

            if (eco != null && eco.Machines.TryGetValue(b.uid, out var m))
            {
                var makes = PlaySection(row, p, "MAKES");
                makes.style.marginLeft = p.P(26);
                var flow = PK.Row(makes, p.P(10));
                var input = m.Recipe.Inputs.Count > 0 ? m.Recipe.Inputs[0].item : "";
                PlayInSlot(flow, p, PlayGood(input), "", 90, 56);
                PlayArrow(flow, p, 26, 36);
                PlayInSlot(flow, p, PlayGood(m.Recipe.Output), "", 90, 56);
                PK.N(makes, p, string.Join(" + ", m.Recipe.Inputs.Select(i => $"{i.count} {i.item.Replace('_', ' ')}")) + " → 1 " + m.Recipe.Output.Replace('_', ' '), 14, GameTheme.T("#9fd0ff"));
            }

            PK.Grow(row);
            var right = PK.Col(row, p.P(10));
            right.style.alignItems = Align.FlexEnd;
            right.style.justifyContent = Justify.Center;
            var note = PK.Row(right, p.P(8));
            note.style.backgroundColor = GameTheme.Shade(.2f);
            Kit.Round(note, p.U(10));
            PK.Pad(note, p.P(12), p.P(8), p.P(12), p.P(8));
            PK.Img(note, "Icon_ImageIcon_Lock01_s", p.P(28));
            PK.N(note, p, "You can look, not touch", 16, GameTheme.C("#dff1ff"));
            var entry = s.SelectedEntry;
            PlayBtn(right, p, "yellow", "Icon_Hammer", "Build one too", 260, 64, 22, () =>
            {
                if (entry != null) _playBuildCat = PlayCatOf(entry);
                s.Deselect();
                Go("play", "build");
            });
        }

        // ------------------------------------------------------------------ sailing there

        private void PlayVisitTravel(Ctx c)
        {
            var p = c.P;
            var owner = PlayVisitOf(c.Args);
            if (Sandbox != null) Sandbox.Lobby = true;
            var bg = PK.Inset(PK.Box(c.Root));
            bg.pickingMode = PickingMode.Position;
            bg.style.backgroundColor = GameTheme.T("#0b2a52");
            Kit.Cover(bg, GameArt.Get("Play_VisitBg"));   // the sea map with the other farms
            PK.Dim(bg, .45f);
            var col = PK.Col(c.Root, p.P(18));
            col.style.position = Position.Absolute;
            col.style.left = new Length(50, LengthUnit.Percent);
            col.style.top = new Length(50, LengthUnit.Percent);
            col.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            col.style.alignItems = Align.Center;
            var ring = PK.Box(col);
            PK.Size(ring, p.P(110), p.P(110));
            Kit.Round(ring, p.P(110));
            ring.style.backgroundColor = GameTheme.T("#1a3878");
            Kit.Border(ring, p.U(3), PK.Ink);
            var spin = PK.Inset(PK.Box(ring), p.P(22), p.P(22), p.P(22), p.P(22));
            Kit.Contain(spin, GameArt.Get("Loading_Rotate"));
            PK.T(col, p, $"Sailing to {owner.Name}'s island…", 34);
            PK.N(col, p, $"Island Lv {owner.Level} · {PK.Num(owner.Worth)} net worth", 17, GameTheme.T("#9fd0ff"));
            var until = Time.unscaledTime + 1.6f;
            PlayVisitLoad(owner);
            c.Live(() =>
            {
                spin.style.rotate = new Rotate(new Angle(Time.unscaledTime * 360f % 360f, AngleUnit.Degree));
                if (Time.unscaledTime >= until && owner.Loaded) Go("visit", owner);
            });
        }

        // ------------------------------------------------------------------ pick a farm

        private void PlayVisitPick(Ctx c)
        {
            var p = c.P;
            var here = c.Args is PlayVisitTarget t ? t.Uid : null;
            var pop = PK.Popup(c.Root, p, 760, 0, "Visit a farm", Back, pad: 26);
            PK.Seg(pop, p, new[] { ("fr", "Friends"), ("top", "Top farms"), ("rand", "Random") }, _playVisitTab, id =>
            {
                _playVisitTab = id;
                MarkDirty();
            }, 24, 58);
            var list = PK.Col(pop, p.P(12));
            list.style.marginTop = p.P(14);
            var farms = PlayVisitFarms(_playVisitTab);
            if (farms.Count == 0)
                PK.N(list, p, _playVisitTab == "fr" ? "Add friends to visit their farms." : "No farms on this week's board yet.", 18, GameTheme.T("#bfe6ff"), TextAnchor.MiddleCenter);
            foreach (var f in farms)
            {
                var cur = f.Uid == here;
                var r = PK.ListRow(list, p, cur);
                r.style.height = p.P(96);
                PK.Pad(r, p.P(14), 0, p.P(14), 0);
                PlayAvatar(r, p, f.Avatar, 70);
                var w = PK.Col(r, p.P(6));
                w.style.flexGrow = 1;
                w.style.marginLeft = p.P(14);
                PK.T(w, p, f.Name, 26);
                PK.N(w, p, "Island Lv " + f.Level, 15, GameTheme.T("#bfe6ff"));
                var worth = PK.Row(r, p.P(6));
                PK.Img(worth, "ResourceBar_Icon_Gold", p.P(36));
                PK.T(worth, p, PK.Num(f.Worth), 22);
                var target = f;
                var btn = cur ? PK.Btn(r, p, "gray", "Here", 150, 60, 22) : PK.Btn(r, p, "green", "Visit", 150, 60, 24, () => { Sandbox?.EndVisit(); Go("visittravel", target); });
                btn.style.marginLeft = p.P(14);
            }
        }

        // ------------------------------------------------------------------ the owner

        private void PlayVisitProfile(Ctx c)
        {
            var p = c.P;
            var o = PlayVisitOf(c.Args);
            var pop = PK.Popup(c.Root, p, 860, 0, null, Back);
            var head = PK.Row(pop, p.P(18));
            PK.Pad(head, 0, p.P(18), 0, p.P(14));
            PlayAvatar(head, p, o.Avatar, 120);
            var words = PK.Col(head, p.P(8));
            words.style.flexGrow = 1;
            var nm = PK.Row(words, p.P(10));
            PK.T(nm, p, o.Name, 36);
            Kit.Round(PK.Img(nm, "Icon_Flag_" + o.Flag, p.P(30)), p.U(4));
            PK.N(words, p, $"{o.Farm} · Island Lv {o.Level} · {FarmTitle(o.Worth)}", 17, GameTheme.T("#9fd0ff"));

            var stats = new (string icon, string label, string value)[]
            {
                ("ResourceBar_Icon_Gold", "Net worth", PK.Num(o.Worth)), ("Icon_ImageIcon_Medal_Silver", "World rank", o.Rank), ("Icon_Heart", "Likes", PK.Num(o.Likes)),
                ("Icon_Hammer", "Buildings", PK.Num(o.Buildings)), ("G_Wheat", "Crops grown", PK.Num(o.Crops)), ("Icon_Shield", "Raids beaten", PK.Num(o.Raids)),
            };
            for (var r = 0; r < 2; r++)
            {
                var row = PK.Row(pop, p.P(12));
                if (r > 0) row.style.marginTop = p.P(12);
                for (var k = 0; k < 3; k++)
                {
                    var (icon, label, value) = stats[r * 3 + k];
                    var cell = PK.Row(row, p.P(10));
                    cell.style.flexGrow = 1;
                    cell.style.flexBasis = 0;
                    cell.style.backgroundColor = GameTheme.T("#0b3a6e");
                    Kit.Round(cell, p.U(10));
                    PK.Pad(cell, p.P(10), p.P(10), p.P(10), p.P(10));
                    PK.Img(cell, icon, p.P(46));
                    var w = PK.Col(cell, p.P(4));
                    PK.N(w, p, label, 14, GameTheme.T("#9fd0ff"));
                    PK.T(w, p, value, 24);
                }
            }

            var buttons = PK.Row(pop, p.P(16));
            buttons.style.justifyContent = Justify.Center;
            buttons.style.marginTop = p.P(18);
            PlayBtn(buttons, p, "blue", "Icon_MenuIcon04_Friends", "Add friend", 250, 70, 24, () => Open("friends"));
            PK.Btn(buttons, p, "green", "Back to visit", 250, 70, 24, Back);
        }
    }
}
