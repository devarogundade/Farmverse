#nullable enable
using System;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The inbox page (inbox.html): the kit popup over a dark veil with a title and unread count, a bar with
    // "Claim All" and delete (tablet; in the title on the phone), and message rows: slanted coloured
    // portrait, headline and line, reward slot and Claim / Go / View. Sizes are the mockup's own pixels.
    // The messages are the player's notifications on the server (InboxService): the team's mail, referral
    // and weekly-prize notices, friends joining. A message's reward is a server reward (rewards-claim: gems
    // go to the server wallet, coins to the island); read marks and deletes go to the server too.
    public sealed partial class GameUi
    {
        private static readonly bool RegLbInbox = Register("inbox", UiLayer.Page, (ui, c) => ui.LbInbox(c), 1f);

        private sealed class LbMsg
        {
            public string Id = "";
            public string? RewardId;
            public bool Read;
            public string Portrait = "";
            public string Colour = "blue";
            public string Head = "";
            public string Line = "";
            public string? Expires;
            public (string icon, string label, int coins, int gems)[]? Rewards;
            public string Action = "view";   // claim, go, view
            public string? GoTo;             // a screen id for Go
            public bool IsNew;
        }

        // ---- the server's messages ---------------------------------------------------------------------------

        /// <summary>How each kind of message looks and reads (texts are filled from the message's params).</summary>
        private static (string portrait, string colour, string head, string line) LbLook(NotificationKind kind) => kind switch
        {
            NotificationKind.FriendJoined => ("Icon_Friends", "blue", "A friend joined", "{name} joined your farm circle."),
            NotificationKind.ReferralReward => ("Icon_Gift", "green", "Referral reward!", "{name} reached level {level}: collect {coins} coins and {gems} gems."),
            NotificationKind.LeaderboardPrize => ("Icon_ImageIcon_Medal_Silver", "purple", "Weekly results are in", "You finished #{rank} this week!"),
            NotificationKind.Mail => ("Icon_Megaphone", "yellow", "{title}", "{body}"),
            _ => ("Icon_Megaphone", "blue", "Farmverse", "{message}"),
        };

        private static string LbFill(string text, IReadOnlyDictionary<string, string> args)
        {
            foreach (var kv in args) text = text.Replace("{" + kv.Key + "}", kv.Value);
            return text;
        }

        /// <summary>The inbox as rows: reward messages say what they pay; routes become Go.</summary>
        private static List<LbMsg> LbMail
        {
            get
            {
                var list = new List<LbMsg>();
                var inbox = Inbox;
                if (inbox == null) return list;
                var pending = new HashSet<string>(Rewards?.Pending.Select(r => r.Id) ?? Enumerable.Empty<string>());
                foreach (var (id, d) in inbox.Messages)
                {
                    var look = LbLook(d.Kind);
                    d.RouteArgs.TryGetValue("rewardId", out var rewardId);
                    var m = new LbMsg
                    {
                        Id = id, Portrait = look.portrait, Colour = look.colour, Head = LbFill(look.head, d.Params), Line = LbFill(look.line, d.Params),
                        Read = d.ReadAt > 0, IsNew = d.ReadAt == 0, RewardId = string.IsNullOrEmpty(rewardId) ? null : rewardId,
                    };
                    if (m.RewardId != null)
                    {
                        int.TryParse(d.Params.TryGetValue("coins", out var c) ? c : "0", out var coins);
                        int.TryParse(d.Params.TryGetValue("gems", out var g) ? g : "0", out var gems);
                        var parts = new List<(string, string, int, int)>();
                        if (coins > 0) parts.Add(("ResourceBar_Icon_Gold", PK.Num(coins), coins, 0));
                        if (gems > 0) parts.Add(("ResourceBar_Icon_Gem", PK.Num(gems), 0, gems));
                        m.Rewards = parts.ToArray();
                        m.Action = pending.Contains(m.RewardId) ? "claim" : "view";
                        if (!pending.Contains(m.RewardId)) m.Read = true;   // collected: done
                    }
                    else if (!string.IsNullOrEmpty(d.Route))
                    {
                        m.Action = "go";
                        m.GoTo = d.Route;
                    }
                    list.Add(m);
                }
                return list;
            }
        }

        private static readonly Dictionary<string, (string top, string bottom)> LbPortraitColours = new Dictionary<string, (string, string)>
        {
            ["blue"] = ("#2a9cf0", "#1272cc"), ["green"] = ("#21c27a", "#0f9a5d"), ["orange"] = ("#ffa63a", "#f07a10"),
            ["purple"] = ("#b05cff", "#7d2fe0"), ["red"] = ("#ff6b6b", "#d63a3a"), ["yellow"] = ("#ffd84a", "#f2a600"),
        };

        /// <summary>Messages done with: read, and with nothing left to collect.</summary>
        private static HashSet<string> LbDone => new HashSet<string>(LbMail.Where(m => m.Read && m.Action != "claim").Select(m => m.Id));

        /// <summary>Nothing is hidden on the device any more: deleted messages are gone from the server.</summary>
        private static HashSet<string> LbGone => new HashSet<string>();

        /// <summary>Messages to look at: unread, or a reward to collect (the inbox badge on Home and the title).</summary>
        public static int LbUnclaimed => LbMail.Count(m => !m.Read || m.Action == "claim");

        private bool _lbBusy;

        /// <summary>Collect a message's reward (rewards-claim): gems land on the server, coins on the island.</summary>
        private async System.Threading.Tasks.Task<(int coins, int gems)> LbCollect(LbMsg m)
        {
            var rewards = Rewards;
            if (m.RewardId == null || rewards == null) return (0, 0);
            var result = await rewards.Claim(m.RewardId);
            if (result.IsFailure || !result.Value.Fresh) return (0, 0);
            var coins = (int)result.Value.Coins;
            if (coins > 0) Economy?.AddCoins(coins);
            return (coins, (int)result.Value.Gems);
        }

        private async void LbAct(LbMsg m)
        {
            if (_lbBusy) return;
            var inbox = Inbox;
            if (inbox == null) return;
            _lbBusy = true;
            try
            {
                if (m.Action == "claim")
                {
                    var (coins, gems) = await LbCollect(m);
                    if (coins + gems > 0)
                    {
                        Farmverse.Presentation.World.Island.IslandAudio.Play("coin");
                        Say((coins > 0 ? "+" + PK.Num(coins) + " coins  " : "") + (gems > 0 ? "+" + gems + " gems" : ""));
                    }
                    else Say("Couldn't collect it — check your connection and try again");
                }
                if (!m.Read) _ = inbox.MarkRead(m.Id);
            }
            finally
            {
                _lbBusy = false;
            }

            if (m.Action == "go" && m.GoTo != null)
            {
                if (m.GoTo == "play" || m.GoTo == "home") Go(m.GoTo);
                else if (Registry.ContainsKey(m.GoTo)) Open(m.GoTo);
                return;
            }

            MarkDirty();
        }

        private async void LbClaimAll()
        {
            if (_lbBusy) return;
            _lbBusy = true;
            int coins = 0, gems = 0;
            try
            {
                foreach (var m in LbMail.Where(m => m.Action == "claim").ToList())
                {
                    var (c, g) = await LbCollect(m);
                    coins += c;
                    gems += g;
                    if (!m.Read) _ = Inbox?.MarkRead(m.Id);
                }
            }
            finally
            {
                _lbBusy = false;
            }
            if (coins + gems > 0)
            {
                Farmverse.Presentation.World.Island.IslandAudio.Play("coin");
                Say((coins > 0 ? "+" + PK.Num(coins) + " coins  " : "") + (gems > 0 ? "+" + gems + " gems" : ""));
            }
            MarkDirty();
        }

        /// <summary>Delete what's done (inbox-delete; the server keeps any with a reward still to collect).</summary>
        private async void LbDeleteRead()
        {
            var inbox = Inbox;
            var ids = LbDone.ToList();
            if (inbox == null || ids.Count == 0) return;
            var result = await inbox.Delete(ids.Take(InboxService.PageSize).ToList());
            if (result.IsFailure) Say("Couldn't delete — check your connection");
            MarkDirty();
        }

        // ---- the page ------------------------------------------------------------------------------------

        private VisualElement LbBtn(VisualElement? parent, Pt p, string colour, float h, float w, string? label, float fs, Action click)
        {
            var b = PK.Centre(PK.Row(parent));
            b.style.height = p.U(h);
            b.style.flexShrink = 0;
            if (w > 0) b.style.width = p.U(w);
            var art = colour == "g" ? "Button01_195_Green" : colour == "b" ? "Button01_195_Blue" : "Button01_195_Gray";
            Kit.Nine(b, GameArt.Get(art), 30, 96, 30, 96, p.U(h) / 2f / 96f);
            if (label != null) LbT(b, p, label, fs, 4, colour == "gr" ? GameTheme.C("#e6e9f2") : Color.white);
            PK.Tap(b, p, click);
            return b;
        }

        private void LbInbox(Ctx c)
        {
            var p = c.P;
            c.Watch(() => (Inbox?.Version.Value ?? 0) * 7919 + (Rewards?.Version.Value ?? 0));
            var ph = p.Phone;
            float pw = ph ? 730 : 1120, phh = ph ? 340 : 860, shift = ph ? -6 : 14, pp = ph ? 14 : 34, cs = ph ? 44 : 76, th = ph ? 48 : 84,
                tfs = ph ? 26 : 44, barH = ph ? 0 : 92, infoFs = ph ? 12 : 22, bfs = ph ? 18 : 30, rh = ph ? 64 : 112, rgap = ph ? 9 : 16,
                hfs = ph ? 17 : 28, sfs = ph ? 11 : 18, slot = ph ? 70 : 118, aw = ph ? 112 : 200;

            var dim = PK.Inset(PK.Box(c.Root));
            dim.pickingMode = PickingMode.Position;
            dim.style.backgroundColor = GameTheme.T("#040e24", .72f);

            var pop = PK.Col(c.Root);
            pop.pickingMode = PickingMode.Position;
            pop.style.position = Position.Absolute;
            pop.style.width = p.U(pw);
            pop.style.height = p.U(phh);
            pop.style.left = (p.W - p.U(pw)) / 2f;
            pop.style.top = (p.H - p.U(phh)) / 2f + p.U(shift);
            Kit.Nine(pop, GameArt.Get("Popup03_Single"), 18, 24, 18, 30, p.U(22) / 24f);
            PK.Pad(pop, p.U(pp), 0, p.U(pp), p.U(26));

            var done = LbDone;
            var gone = LbGone;

            // title with the unread count (and the phone's buttons)
            var ttl = PK.Centre(PK.Row(pop));
            ttl.style.height = p.U(th);
            ttl.style.flexShrink = 0;
            ttl.style.marginLeft = -p.U(pp);
            ttl.style.marginRight = -p.U(pp);
            ttl.style.borderBottomWidth = p.U(3);
            ttl.style.borderBottomColor = GameTheme.Shade(.18f);
            LbT(ttl, p, "Inbox", tfs, 5);
            var count = LbUnclaimed;
            if (count > 0)
            {
                var cnt = PK.Centre(PK.Abs(PK.Box(ttl), top: p.U(th * .18f), height: p.U(tfs * .75f)));
                cnt.style.left = p.U(pw) / 2f + p.U(tfs * 1.3f);
                cnt.style.minWidth = p.U(tfs * .75f);
                PK.Pad(cnt, p.U(5), 0, p.U(5), 0);
                cnt.style.backgroundColor = GameTheme.C("#ff3a52");
                Kit.Round(cnt, p.U(tfs));
                Kit.Border(cnt, p.U(2), Color.white);
                var cl = PK.TL(cnt, p, count.ToString(), tfs * .42f);
                cl.style.unityTextOutlineWidth = 0;
                cl.style.textShadow = new TextShadow();
            }

            if (ph)
            {
                var btns = PK.Row(ttl);
                btns.style.position = Position.Absolute;
                btns.style.right = p.U(pp + 34);
                btns.style.top = (p.U(th) - p.U(32)) / 2f;
                var del = LbBtn(btns, p, "gr", 32, 36, null, 0, LbDeleteRead);
                PK.Img(del, "Icon_PictoIcon_Delete", p.U(32) * .52f);
                var all = LbBtn(btns, p, "g", 32, 0, "Claim All", 15, LbClaimAll);
                PK.Pad(all, p.U(14), 0, p.U(14), 0);
                all.style.marginLeft = p.U(8);
            }
            else
            {
                var bar = PK.Row(pop);
                bar.style.height = p.U(barH);
                bar.style.flexShrink = 0;
                PK.Pad(bar, p.U(2), 0, p.U(2), 0);
                var info = LbN(bar, p, "Pressing <color=#ffd23a>Claim All</color> collects every reward.", infoFs, GameTheme.T("#7fd0ff"));
                info.style.flexGrow = 1;
                info.style.flexShrink = 1;
                var del = LbBtn(bar, p, "gr", barH * .78f, barH * .86f, null, 0, LbDeleteRead);
                del.style.marginLeft = p.U(14);
                PK.Img(del, "Icon_PictoIcon_Delete", p.U(barH * .78f) * .52f);
                var all = LbBtn(bar, p, "g", barH * .78f, 0, "Claim All", bfs, LbClaimAll);
                PK.Pad(all, p.U(bfs * 1.1f), 0, p.U(bfs * 1.1f), 0);
                all.style.marginLeft = p.U(14);
            }

            var x = PK.Centre(PK.Abs(PK.Box(pop), top: -p.U(cs * .3f), width: p.U(cs), height: p.U(cs * 1.1f), right: -p.U(cs * .35f)));
            Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
            PK.Img(x, "Icon_PictoIcon_Close", p.U(cs) * .52f * 53f / 49f, p.U(cs) * .52f).style.marginTop = -p.U(cs) * .08f;
            PK.Tap(x, p, Back, "whoosh");

            // the messages: rewards first, then news, then the done ones
            var live = LbMail.Where(m => !gone.Contains(m.Id))
                .OrderBy(m => done.Contains(m.Id) ? 2 : m.Action == "claim" ? 0 : 1).ToList();
            if (live.Count == 0)
            {
                var empty = PK.Centre(PK.Col(pop));
                empty.style.flexGrow = 1;
                var mail = PK.Img(empty, "Icon_Mail", p.U(rh * 1.2f));
                mail.style.opacity = .9f;
                LbT(empty, p, "No new messages", hfs, 3, GameTheme.T("#cfe8ff")).style.marginTop = p.U(10);
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            PK.Pad(scroll.contentContainer, p.U(2), p.U(2), p.U(2), p.U(10));
            pop.Add(scroll);
            var first = true;
            foreach (var m in live)
            {
                var row = LbMailRow(scroll.contentContainer, p, m, done.Contains(m.Id), ph, rh, hfs, sfs, slot, aw);
                if (!first) row.style.marginTop = p.U(rgap);
                first = false;
            }
        }

        private VisualElement LbMailRow(VisualElement parent, Pt p, LbMsg m, bool isDone, bool ph, float rh, float hfs, float sfs, float slot, float aw)
        {
            var row = PK.ListRow(parent, p);
            row.style.height = p.U(rh);
            PK.Clip(row);
            if (isDone)
            {
                row.style.opacity = .8f;
                row.style.unityBackgroundImageTintColor = new Color(.62f, .65f, .7f);
            }

            // slanted coloured portrait
            var (top, bottom) = LbPortraitColours.TryGetValue(m.Colour, out var pc) ? pc : LbPortraitColours["blue"];
            var por = PK.Centre(PK.Box(row));
            por.style.width = p.U(rh * 1.25f);
            por.style.height = new Length(100, LengthUnit.Percent);
            por.style.flexShrink = 0;
            por.style.paddingRight = p.U(rh * .22f);
            Kit.Picture(por, LbSlant("lbpor_" + m.Colour + (isDone ? "_d" : ""), isDone ? LbGrey(GameTheme.C(top)) : GameTheme.C(top), isDone ? LbGrey(GameTheme.C(bottom)) : GameTheme.C(bottom), .82f));
            PK.Img(por, m.Portrait, p.U(rh * .66f));

            var msg = PK.Col(row);
            msg.style.flexGrow = 1;
            msg.style.flexShrink = 1;
            msg.style.minWidth = 0;
            msg.style.overflow = Overflow.Hidden;
            PK.Pad(msg, p.U(rh * .18f), 0, p.U(rh * .18f), 0);
            var h = LbT(msg, p, m.Head, hfs, 3.5f);
            h.style.textOverflow = TextOverflow.Ellipsis;
            h.style.overflow = Overflow.Hidden;
            var s = LbN(msg, p, m.Line, sfs, GameTheme.T("#8ad6ff"));
            s.style.textOverflow = TextOverflow.Ellipsis;
            s.style.overflow = Overflow.Hidden;
            s.style.marginTop = p.U(rh * .07f);
            if (!ph && m.Expires != null) LbN(msg, p, "Expires in " + m.Expires, sfs * .82f, GameTheme.C("#ffb15c")).style.marginTop = p.U(rh * .07f);

            // the reward slot (one or two rewards)
            if (m.Rewards != null)
            {
                var multi = m.Rewards.Length > 1;
                var sl = PK.Centre(multi ? PK.Row(row) : PK.Col(row));
                sl.style.width = p.U(multi ? slot * 1.9f : slot);
                sl.style.height = p.U(rh - 14);
                sl.style.flexShrink = 0;
                sl.style.marginRight = p.U(rh * .18f);
                Kit.Nine(sl, GameArt.Get("ItemFrame03_Single_Navy"), 20, 20, 20, 24, p.U(14) / 20f);
                var firstR = true;
                foreach (var (icon, label, _, _) in m.Rewards)
                {
                    var cell = multi ? PK.Centre(PK.Col(sl)) : sl;
                    if (multi)
                    {
                        cell.style.height = new Length(100, LengthUnit.Percent);
                        if (!firstR) cell.style.marginLeft = p.U(8);
                    }

                    firstR = false;
                    PK.Img(cell, icon, p.U(rh - 14) * (multi ? .46f : .52f));
                    LbT(cell, p, label, rh * .24f, 3).style.marginTop = p.U(2);
                }
            }

            // the action
            VisualElement act;
            if (isDone) act = LbBtn(row, p, "gr", rh * .62f, aw, m.Action == "go" || m.Action == "view" ? "Read" : "Claimed", hfs, () => { });
            else if (m.Action == "claim")
                act = LbBtn(row, p, "g", rh * .62f, aw, "Claim", hfs, () => LbAct(m));
            else act = LbBtn(row, p, "b", rh * .62f, aw, m.Action == "go" ? "Go" : "View", hfs, () => LbAct(m));

            act.style.marginRight = p.U(rh * .2f);
            if (isDone) act.pickingMode = PickingMode.Ignore;

            if (m.IsNew && !isDone)
            {
                var tag = PK.Abs(PK.Centre(PK.Box(row)), left: p.U(6), top: p.U(6));
                tag.style.backgroundColor = GameTheme.C("#ff3a52");
                Kit.Border(tag, p.U(2), Color.white);
                Kit.Round(tag, p.U(7));
                PK.Pad(tag, p.U(6), p.U(2), p.U(6), p.U(3));
                LbT(tag, p, "NEW", rh * .16f, 2.5f);
            }

            return row;
        }

        private static Color LbGrey(Color c)
        {
            var g = c.grayscale;
            return Color.Lerp(new Color(g, g, g, c.a), c, .35f) * .8f;
        }

        private static readonly Dictionary<string, Texture2D> LbTextures = new Dictionary<string, Texture2D>();

        /// <summary>A top-to-bottom gradient cut on the right: full width at the top, <paramref name="bottomWidth"/> at the bottom (CSS clip-path).</summary>
        private static Texture2D LbSlant(string key, Color top, Color bottom, float bottomWidth)
        {
            if (LbTextures.TryGetValue(key, out var t) && t != null) return t;
            const int w = 100, h = 80;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < h; y++)
            {
                var f = y / (h - 1f);
                var edge = w * Mathf.Lerp(1f, bottomWidth, f);
                var c = Color.Lerp(top, bottom, f);
                for (var x = 0; x < w; x++)
                {
                    var o = c;
                    o.a *= Mathf.Clamp01(edge - x);
                    t.SetPixel(x, h - 1 - y, o);
                }
            }

            t.Apply();
            LbTextures[key] = t;
            return t;
        }
    }
}
