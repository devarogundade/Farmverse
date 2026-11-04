#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Farmverse.Application.Features;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The leaderboard page (leaderboard.html, after the pack's Ranking screen): transparent header with the
    // weekly reset timer, World · Country · Friends tabs, your row pinned in sky blue, list rows with the rank
    // cell, avatar, flag, name and net worth; on the tablet a side column with "My farm" and the weekly prizes,
    // on the phone your row opens the net-worth breakdown. Sizes are the mockup's own pixels (tablet frame
    // 1366 wide, phone 844), so everything goes through Pt.U.
    // The rows are the server's weekly board (LeaderboardService: net worth as the island engine computed it
    // the last time it ran each island); your own row shows the island's live net worth.
    public sealed partial class GameUi
    {
        private static readonly bool RegLbLeaderboard = Register("leaderboard", UiLayer.Page, (ui, c) => ui.LbLeaderboard(c), 1f);

        private string LbTab = "world";
        private bool LbWorthOpen;

        private sealed class LbRow
        {
            public string Rank = "";
            public int RankNo;
            public string Name = "";
            public long Worth;
            public string Ava = "Icon_Pumkin";
            public string Flag = "Eng";
            public int Lvl;
            public int Move;
            public string Uid = "";
            public bool Me;
            public bool Visit;
            public string Farm = "";
        }

        private static string LbAvatar => PlayerPrefs.GetString("fv_avatar", "Icon_Pumkin");

        private LbRow LbMine(string rank)
        {
            var eco = Economy;
            return new LbRow
            {
                Rank = rank,
                Name = PlayerName,
                Worth = eco?.NetWorth ?? 0,
                Ava = LbAvatar,
                Flag = Language,
                Lvl = eco?.IslandLevel ?? 1,
                Me = true,
                Farm = FarmTitle(eco?.NetWorth ?? 0),
            };
        }

        private static LbRow LbFrom(BoardRow r, int rank) => new LbRow
        {
            Uid = r.Uid, RankNo = rank, Rank = rank.ToString(), Name = r.Name, Worth = r.Worth, Ava = AvatarArtOf(r.AvatarId),
            Flag = FlagOfIso(r.Locale), Lvl = r.Level, Farm = FarmTitle(r.Worth),
        };

        private List<LbRow> LbRows(string tab)
        {
            var list = new List<LbRow>();
            var board = Board;
            if (board == null) return list;
            if (tab == "friends")
            {
                var friends = Friends?.Friends;
                if (friends != null) board.Follow(friends.Select(f => f.Uid));
                var all = board.Friends.Select(f => LbFrom(f, 0)).ToList();
                foreach (var row in all) row.Visit = true;
                all.Add(LbMine(""));
                all.Sort((a, b) => b.Worth.CompareTo(a.Worth));
                for (var i = 0; i < all.Count; i++)
                {
                    all[i].RankNo = i + 1;
                    all[i].Rank = (i + 1).ToString();
                }
                return all;
            }

            var rows = tab == "country" ? board.Country : board.World;
            // your own row is pinned above the list (with your place), so the list leaves it out
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Uid != LbMyUid) list.Add(LbFrom(rows[i], i + 1));
            return list;
        }

        private static string? LbMyUid => Board?.Mine?.Uid;

        /// <summary>My place on a tab ("—" when I'm not in its top 50 or haven't played this week).</summary>
        private string LbMyRank(string tab)
        {
            var board = Board;
            if (board?.Mine == null) return "—";
            var rows = tab == "country" ? board.Country : board.World;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Uid == board.Mine.Uid) return (i + 1).ToString();
            return rows.Count >= LeaderboardService.PageSize ? "50+" : "—";
        }

        /// <summary>Time to the weekly reset (the server's week: Monday 00:00 UTC).</summary>
        private static string LbResetIn()
        {
            var nowMs = (long)(Farmverse.Presentation.World.Island.IslandEconomyHost.Clock * 1000);
            var ends = Board?.WeekEndsAtMs ?? nowMs;
            var left = TimeSpan.FromMilliseconds(Math.Max(0, ends - nowMs));
            return left.TotalHours >= 24 ? $"{(int)left.TotalDays}d {left.Hours}h" : $"{left.Hours}h {left.Minutes}m";
        }

        // ---- text in the mockup's literal pixels ---------------------------------------------------------

        private static Label LbT(VisualElement? parent, Pt p, string text, float fs, float sw, Color? colour = null)
        {
            var l = PK.TL(parent, p, text, fs, sw);
            if (colour != null) l.style.color = colour.Value;
            return l;
        }

        private static Label LbN(VisualElement? parent, Pt p, string text, float fs, Color colour)
        {
            var l = PK.N(parent, p, text, 1, colour);
            l.style.fontSize = p.U(fs);
            return l;
        }

        // ---- the page ------------------------------------------------------------------------------------

        private void LbLeaderboard(Ctx c)
        {
            var p = c.P;
            var tablet = !p.Phone;
            float hh = tablet ? 96 : 52, th = tablet ? 70 : 38, tfs = tablet ? 30 : 17, pad = tablet ? 24 : 8, gap = tablet ? 22 : 0,
                rh = tablet ? 84 : 54, rankW = tablet ? 92 : 60, rgap = tablet ? 12 : 6, rad = tablet ? 14 : 10;
            var safeL = p.L;                                  // the device's safe area on every device (iPad home bar too)
            var safeR = p.R + (tablet ? p.U(24) : 0f);
            var safeB = p.B;

            // background: the page's own blue glow
            var screen = PK.Inset(PK.Col(c.Root));
            screen.pickingMode = PickingMode.Position;
            Kit.Picture(screen, GameArt.Radial("lbbg_" + GameTheme.Current, .5f, .18f, .7f, .6f,
                (0f, GameTheme.T("#1a86d6")), (.45f, GameTheme.T("#0f62b0")), (1f, GameTheme.T("#0a4888"))));

            // header: back · title · reset timer · home
            var hdr = PK.Row(screen);
            hdr.style.height = p.U(hh);
            hdr.style.flexShrink = 0;
            hdr.style.paddingRight = safeR;
            var back = PK.Centre(PK.Box(hdr));
            PK.Size(back, p.U(hh * 1.25f) + safeL, p.U(hh * .8f));
            back.style.paddingLeft = safeL;
            Kit.Flush(back, true);
            PK.Img(back, "Icon_PictoIcon_Back", p.U(hh * .8f) * .52f);
            PK.Tap(back, p, Back, "whoosh");
            LbT(hdr, p, "Leaderboard", hh * .48f, 5).style.marginLeft = p.U(hh * .35f);
            PK.Grow(hdr);
            var reset = PK.Row(hdr);
            reset.style.marginRight = p.U(hh * .4f);
            PK.Img(reset, "Icon_PictoIcon_Timer", p.U(hh * .5f));
            var rcol = PK.Col(reset);
            rcol.style.marginLeft = p.U(8);
            LbT(rcol, p, "Resets In", hh * .2f, 3);
            var timer = LbT(rcol, p, LbResetIn(), hh * .3f, 3, GameTheme.C("#ff5a7a"));
            timer.style.marginTop = p.U(3);
            c.Live(() => timer.text = Loc.Of(LbResetIn()));
            var home = PK.Img(hdr, "Icon_PictoIcon_Home", p.U(hh * .6f));
            PK.Tap(home, p, () => Go("home"), "whoosh");

            // body: the list column (and the side column on the tablet)
            var body = PK.Row(screen);
            body.style.flexGrow = 1;
            body.style.alignItems = Align.Stretch;
            body.style.minHeight = 0;
            PK.Pad(body, safeL + p.U(pad), 0, safeR + p.U(pad), safeB + p.U(pad));

            var listCol = PK.Col(body);
            listCol.style.flexGrow = 1;
            listCol.style.minWidth = 0;
            if (tablet) listCol.style.maxWidth = p.U(900);
            listCol.style.marginLeft = StyleKeyword.Auto;
            listCol.style.marginRight = StyleKeyword.Auto;

            // tabs
            var seg = PK.Row(listCol);
            seg.style.height = p.U(th);
            seg.style.flexShrink = 0;
            seg.style.backgroundColor = GameTheme.T("#08284f");
            Kit.Round(seg, p.U(rad));
            PK.DropEdge(seg, p.U(4), GameTheme.Shade(.25f));
            seg.style.marginBottom = p.U(rgap);
            foreach (var (id, label, icon) in new[] { ("world", "World", "Icon_Compass"), ("country", "Country", ""), ("friends", "Friends", "Icon_Friends") })
            {
                var on = LbTab == id;
                var tab = PK.Centre(PK.Row(seg));
                tab.style.flexGrow = 1;
                tab.style.flexBasis = 0;
                tab.style.height = new Length(100, LengthUnit.Percent);
                tab.style.justifyContent = Justify.Center;
                if (tablet && icon != "") PK.Img(tab, icon, p.U(th * .55f)).style.marginRight = p.U(8);
                if (id == "country")
                {
                    var fl = PK.Img(tab, "Icon_Flag_" + Language, p.U(th * .42f));
                    Kit.Round(fl, p.U(4));
                    fl.style.marginRight = p.U(8);
                }

                LbT(tab, p, label, tfs, 4, on ? Color.white : GameTheme.T("#3cb4ff"));
                if (on)
                {
                    var u = PK.Abs(PK.Box(tab), bottom: 0, height: Mathf.Max(p.U(4), p.U(th * .07f)));
                    u.style.left = new Length(30, LengthUnit.Percent);
                    u.style.right = new Length(30, LengthUnit.Percent);
                    u.style.backgroundColor = Color.white;
                    Kit.Round(u, p.U(6), p.U(6), 0, 0);
                }

                var tid = id;
                PK.Tap(tab, p, () =>
                {
                    LbTab = tid;
                    MarkDirty();
                });
            }

            // your row pinned on top (world, country), then the scrolling list
            var rows = LbRows(LbTab);
            if (LbTab != "friends")
            {
                var mine = LbMine(LbMyRank(LbTab));
                var pin = LbRowView(listCol, p, c, mine, rh, rankW, tablet, !tablet);
                pin.style.marginBottom = p.U(rgap);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentContainer.style.paddingLeft = p.U(2);
            scroll.contentContainer.style.paddingRight = p.U(2);
            scroll.contentContainer.style.paddingTop = p.U(2);
            scroll.contentContainer.style.paddingBottom = p.U(8);
            listCol.Add(scroll);
            var first = true;
            foreach (var r in rows)
            {
                var v = LbRowView(scroll.contentContainer, p, c, r, rh, rankW, tablet, !tablet && r.Me);
                if (!first) v.style.marginTop = p.U(rgap);
                first = false;
            }

            if (LbTab == "friends")
            {
                var invite = PK.Centre(PK.Row(scroll.contentContainer));
                invite.style.height = p.U(rh);
                invite.style.flexShrink = 0;
                invite.style.marginTop = p.U(rgap);
                Kit.Border(invite, p.U(3), GameTheme.Light(.35f));
                Kit.Round(invite, p.U(14));
                PK.Img(invite, "Icon_PictoIcon_Add01", p.U(rh * .4f)).style.marginRight = p.U(10);
                LbT(invite, p, "Invite friends", rh * .26f, 3, GameTheme.T("#cfe8ff"));
                PK.Tap(invite, p, () => Open("friends"));
            }

            var note = LbN(scroll.contentContainer, p, rows.Count == 0 ? "No one on this week's board yet — play to get on it." : "This week's board · net worth updates whenever your island syncs.", tablet ? 13 : 10, GameTheme.T("#7f9ac7"));
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            note.style.marginTop = p.U(rgap);

            if (tablet) LbSide(body, p, c, gap);
            else if (LbWorthOpen) LbWorthPopup(c.Root, p, c);
        }

        private VisualElement LbRowView(VisualElement parent, Pt p, Ctx c, LbRow r, float rh, float rankW, bool wide, bool info)
        {
            var row = PK.Row(parent);
            row.style.height = p.U(rh);
            row.style.flexShrink = 0;
            Kit.Nine(row, GameArt.Get(r.Me ? "ListFrame03_Single_Bg_Sky" : "ListFrame03_Single_Bg_Blue"), 122, 18, 22, 24, p.U(rankW) / 122f);

            // rank cell (medals for 1–3; your own cell is the brighter blue)
            var rank = PK.Centre(PK.Box(row));
            rank.style.width = p.U(rankW);
            rank.style.flexShrink = 0;
            if (r.Me)
            {
                rank.style.height = p.U(rh - rh * .06f);
                rank.style.alignSelf = Align.FlexStart;
                PK.Grad(rank, "lbmerank", true, (0f, GameTheme.T("#1f8de8")), (1f, GameTheme.T("#1a7bd0")));
                Kit.Round(rank, p.U(12), 0, 0, p.U(12));
            }
            else rank.style.height = new Length(100, LengthUnit.Percent);

            if (r.RankNo >= 1 && r.RankNo <= 3)
                PK.Img(rank, r.RankNo == 1 ? "Icon_ImageIcon_Medal_Gold" : r.RankNo == 2 ? "Icon_ImageIcon_Medal_Silver" : "Icon_ImageIcon_Medal_Bronze", p.U(rh) * .82f);
            else LbT(rank, p, r.Rank, rh * .36f, 4);

            // avatar on the slanted backdrop
            var ava = PK.Centre(PK.Box(row));
            ava.style.width = p.U(rh * 1.15f);
            ava.style.height = p.U(rh - rh * .06f);
            ava.style.alignSelf = Align.FlexStart;
            ava.style.flexShrink = 0;
            ava.style.marginLeft = -p.U(2);
            Kit.Picture(ava, GameArt.Get(r.RankNo % 3 == 2 ? "ListFrame03_Single_Inner_Green" : "ListFrame03_Single_Inner_Blue"));
            var face = PK.Fit(ava, r.Ava, p.U(rh * .94f) * .72f, p.U(rh * .94f) * .72f);
            face.style.marginRight = p.U(rh * 1.15f) * .14f;

            var flag = PK.Img(row, "Icon_Flag_" + r.Flag, p.U(rh * .4f));
            flag.style.marginLeft = p.U(rh * .18f);
            Kit.Round(flag, p.U(5));
            PK.DropEdge(flag, p.U(2), GameTheme.Shade(.25f));

            var who = PK.Col(row);
            who.style.marginLeft = p.U(rh * .18f);
            who.style.minWidth = 0;
            who.style.flexShrink = 1;
            who.style.overflow = Overflow.Hidden;
            LbT(who, p, r.Name + (r.Me ? " (You)" : ""), rh * .27f, 3.5f);
            var sub = LbN(who, p, "Island Lv " + r.Lvl + (wide && r.Farm != "" ? " · " + r.Farm : ""), Mathf.Max(10, rh * .15f), r.Me ? GameTheme.T("#e4f4ff") : GameTheme.T("#9fd0ff"));
            sub.style.marginTop = p.U(5);

            // score: movement chip, net worth, coin
            var score = PK.Row(row);
            score.style.marginLeft = StyleKeyword.Auto;
            score.style.paddingRight = p.U(rh * .3f);
            score.style.flexShrink = 0;
            if (r.Move != 0)
            {
                var mv = LbN(score, p, (r.Move > 0 ? "+" : "-") + Math.Abs(r.Move), Mathf.Max(10, rh * .15f), r.Move > 0 ? GameTheme.C("#71ff4d") : GameTheme.C("#ff7a8c"));
                mv.style.marginRight = p.U(rh * .12f);
                PK.Pad(mv, p.U(7), p.U(2), p.U(7), p.U(2));
                mv.style.backgroundColor = GameTheme.Shade(.2f);
                Kit.Round(mv, p.U(7));
            }

            var worth = LbT(score, p, PK.Num(r.Worth), rh * .36f, 4);
            if (r.Me) c.Live(() => worth.text = Loc.Of(PK.Num(Economy?.NetWorth ?? r.Worth)));
            PK.Img(score, "ResourceBar_Icon_Gold", p.U(rh * .42f)).style.marginLeft = p.U(8);

            if (r.Visit)
            {
                var vh = rh * .56f;
                var visit = PK.Centre(PK.Row(row));
                visit.style.height = p.U(vh);
                visit.style.flexShrink = 0;
                PK.Pad(visit, p.U(rh * .25f), 0, p.U(rh * .25f), 0);
                visit.style.marginRight = p.U(rh * .25f);
                Kit.Nine(visit, GameArt.Get("Button01_145_Green"), 20, 30, 20, 30, p.U(vh) / 2f / 30f);
                LbT(visit, p, "Visit", rh * .22f, 3);
                var who2 = new PlayVisitTarget { Uid = r.Uid, Name = r.Name, Farm = r.Farm, Level = r.Lvl, Worth = r.Worth, Rank = r.Rank, Flag = r.Flag, Avatar = r.Ava };
                PK.Tap(visit, p, () => Go("visittravel", who2));
            }

            if (info) PK.Img(row, "Icon_ImageIcon_Info", p.U(rh * .42f)).style.marginRight = p.U(rh * .2f);

            if (r.Me)
                PK.Tap(row, p, () =>
                {
                    if (p.Phone)
                    {
                        LbWorthOpen = true;
                        MarkDirty();
                    }
                    else Open("profile");
                });
            else if (!r.Visit && r.Uid.Length > 0)
            {
                var who3 = new PlayVisitTarget { Uid = r.Uid, Name = r.Name, Farm = r.Farm, Level = r.Lvl, Worth = r.Worth, Rank = r.Rank, Flag = r.Flag, Avatar = r.Ava };
                PK.Tap(row, p, () => Go("visittravel", who3));
            }
            return row;
        }

        // ---- net worth breakdown -------------------------------------------------------------------------

        private void LbBreakdown(VisualElement parent, Pt p, Ctx c, bool small)
        {
            var eco = Economy;
            var worthNow = eco?.NetWorth ?? 0;
            var coins = (long)(eco?.Coins ?? 0);
            var box = PK.Row(parent);
            box.style.justifyContent = Justify.SpaceBetween;
            box.style.backgroundColor = GameTheme.T("#0b3a6e");
            Kit.Round(box, p.U(12));
            PK.Pad(box, p.U(small ? 12 : 14), p.U(small ? 6 : 10), p.U(small ? 12 : 14), p.U(small ? 6 : 10));
            box.style.marginBottom = p.U(small ? 8 : 12);
            LbN(box, p, "NET WORTH", 13, GameTheme.T("#9fd0ff"));
            var b = PK.Row(box);
            PK.Img(b, "ResourceBar_Icon_Gold", p.U(small ? 24 : 32)).style.marginRight = p.U(8);
            var total = LbT(b, p, PK.Num(worthNow), small ? 22 : 30, 4);
            c.Live(() => total.text = Loc.Of(PK.Num(Economy?.NetWorth ?? 0)));

            // the economy gives coins and the total; buildings, goods and upgrades are counted together for now
            var lines = new[]
            {
                ("ResourceBar_Icon_Gold", "Coins", coins),
                ("Icon_Shop", "Buildings, goods & upgrades", Math.Max(0, worthNow - coins)),
            };
            long max = 1;
            foreach (var l in lines) max = Math.Max(max, l.Item3);
            var bd = PK.Col(parent);
            var firstLine = true;
            foreach (var (icon, label, v) in lines)
            {
                var line = PK.Row(bd);
                if (!firstLine) line.style.marginTop = p.U(small ? 6 : 9);
                firstLine = false;
                var ic = PK.Centre(PK.Box(line));
                ic.style.width = p.U(small ? 24 : 30);
                ic.style.flexShrink = 0;
                PK.Img(ic, icon, p.U(small ? 22 : 28));
                var mid = PK.Col(line);
                mid.style.flexGrow = 1;
                mid.style.marginLeft = p.U(small ? 8 : 10);
                mid.style.marginRight = p.U(small ? 8 : 10);
                LbN(mid, p, label, small ? 12.5f : 14, GameTheme.T("#dfeaff"));
                var bar = PK.Box(mid);
                bar.style.height = p.U(9);
                bar.style.marginTop = p.U(4);
                bar.style.backgroundColor = GameTheme.T("#062040");
                Kit.Round(bar, p.U(6));
                PK.Clip(bar);
                var fill = PK.Abs(PK.Box(bar), left: 0, top: 0, bottom: 0);
                fill.style.width = new Length(v * 100f / max, LengthUnit.Percent);
                Kit.Round(fill, p.U(6));
                PK.Grad(fill, "lbbar", true, (0f, GameTheme.C("#ffd23a")), (1f, GameTheme.C("#f29a00")));
                LbT(line, p, PK.Num(v), 17, 3);
            }
        }

        private void LbSide(VisualElement body, Pt p, Ctx c, float gap)
        {
            var side = PK.Col(body);
            side.style.width = p.U(400);
            side.style.flexShrink = 0;
            side.style.marginLeft = p.U(gap);

            VisualElement Panel(string title)
            {
                var panel = PK.Col(side);
                panel.style.backgroundColor = GameTheme.T("#08284f");
                Kit.Round(panel, p.U(18));
                PK.Pad(panel, p.U(18), p.U(18), p.U(18), p.U(18));
                PK.DropEdge(panel, p.U(5), GameTheme.Shade(.25f));
                var h = LbT(panel, p, title, 22, 3, GameTheme.T("#cfe3ff"));
                h.style.unityTextAlign = TextAnchor.MiddleCenter;
                h.style.marginBottom = p.U(14);
                return panel;
            }

            // my farm
            var eco = Economy;
            var me = Panel("MY FARM");
            var head = PK.Row(me);
            head.style.marginBottom = p.U(14);
            var av = PK.Centre(PK.Box(head));
            PK.Size(av, p.U(86), p.U(86));
            Kit.Round(av, p.U(16));
            Kit.Border(av, p.U(3), PK.Ink);
            PK.Grad(av, "lbmeava", true, (0f, GameTheme.T("#3fb2ff")), (1f, GameTheme.T("#1676d1")));
            PK.Fit(av, LbAvatar, p.U(80) * .72f, p.U(80) * .72f);
            var info = PK.Col(head);
            info.style.marginLeft = p.U(14);
            LbT(info, p, PlayerName, 26, 4);
            var s1 = PK.Row(info);
            s1.style.marginTop = p.U(6);
            var fl = PK.Img(s1, "Icon_Flag_" + Language, p.U(18));
            Kit.Round(fl, p.U(3));
            fl.style.marginRight = p.U(6);
            LbN(s1, p, FarmTitle(eco?.NetWorth ?? 0) + " · Lv " + (eco?.IslandLevel ?? 1), 14, GameTheme.T("#9fd0ff"));
            LbN(info, p, "World rank: " + LbMyRank("world"), 14, GameTheme.T("#9fd0ff")).style.marginTop = p.U(6);
            LbBreakdown(me, p, c, false);

            // weekly prizes: the catalogue's bands, paid by the server when the week closes
            var pr = Panel("WEEKLY PRIZES");
            pr.style.marginTop = p.U(gap);
            var prizes = new List<(string, string, string, string)>();
            var from = 1;
            foreach (var band in Wallet.Prices.LeaderboardPrizes)
            {
                var tierLabel = band.RankTo == 1 ? "medal" : from == band.RankTo ? band.RankTo.ToString() : from + "–" + band.RankTo;
                var label = band.RankTo == 1 ? "Rank 1" : "Top " + PK.Num(band.RankTo);
                var gems = band.Gems > 0;
                prizes.Add((tierLabel, label, gems ? "ResourceBar_Icon_Gem" : "ResourceBar_Icon_Gold", PK.Num(gems ? band.Gems : band.Coins) + (gems && band.Coins > 0 ? " +" + PK.Num(band.Coins) + "c" : "")));
                from = band.RankTo + 1;
            }
            var firstPrize = true;
            foreach (var (tier, label, icon, amount) in prizes)
            {
                var row = PK.Row(pr);
                if (!firstPrize) row.style.marginTop = p.U(9);
                firstPrize = false;
                row.style.backgroundColor = GameTheme.T("#0b3a6e");
                Kit.Round(row, p.U(12));
                PK.Pad(row, p.U(12), p.U(8), p.U(12), p.U(8));
                var t = PK.Centre(PK.Box(row));
                t.style.width = p.U(58);
                t.style.flexShrink = 0;
                if (tier == "medal") PK.Img(t, "Icon_ImageIcon_Medal_Gold", p.U(44));
                else LbT(t, p, tier.Replace("≤", "<"), 20, 3);
                var l = LbT(row, p, label, 18, 3);
                l.style.flexGrow = 1;
                l.style.marginLeft = p.U(12);
                var rw = PK.Row(row);
                PK.Img(rw, icon, p.U(30)).style.marginRight = p.U(6);
                LbT(rw, p, amount, 20, 3);
            }

            var note = LbN(pr, p, prizes.Count == 0 ? "No prizes this week." : "Paid to your mailbox when the week closes.", 12, GameTheme.T("#7f9ac7"));
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            note.style.marginTop = p.U(8);
        }

        /// <summary>Phone: your row opens how net worth is counted.</summary>
        private void LbWorthPopup(VisualElement root, Pt p, Ctx c)
        {
            var dim = PK.Inset(PK.Box(root));
            dim.pickingMode = PickingMode.Position;
            dim.style.backgroundColor = GameTheme.T("#030e22", .7f);
            PK.Centre(dim);
            void Close()
            {
                LbWorthOpen = false;
                MarkDirty();
            }

            dim.RegisterCallback<ClickEvent>(e => { if (e.target == dim) Close(); });
            var pop = PK.Col(dim);
            pop.pickingMode = PickingMode.Position;
            pop.style.width = p.U(440);
            pop.style.backgroundColor = GameTheme.T("#08284f");
            Kit.Round(pop, p.U(18));
            Kit.Border(pop, p.U(3), PK.Ink);
            PK.Pad(pop, p.U(18), p.U(14), p.U(18), p.U(16));
            PK.DropEdge(pop, p.U(6), GameTheme.Shade(.35f));
            var h = LbT(pop, p, "HOW NET WORTH IS COUNTED", 20, 3);
            h.style.unityTextAlign = TextAnchor.MiddleCenter;
            h.style.marginBottom = p.U(10);
            LbBreakdown(pop, p, c, true);

            var x = PK.Centre(PK.Abs(PK.Box(pop), top: -p.U(12), width: p.U(38), height: p.U(38), right: -p.U(12)));
            Kit.Round(x, p.U(19));
            Kit.Border(x, p.U(3), PK.Ink);
            PK.Grad(x, "lbx", true, (0f, GameTheme.C("#ff5a5a")), (1f, GameTheme.C("#d62f3a")));
            PK.Img(x, "Icon_PictoIcon_Close", p.U(18));
            PK.Tap(x, p, Close, "whoosh");
        }
    }
}
