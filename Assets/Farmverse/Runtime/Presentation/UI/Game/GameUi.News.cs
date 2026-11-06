#nullable enable
using System;
using System.Linq;
using Farmverse.Application.Features;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // News: the team's announcements (news/, published with backend tools/publishNews.ts), newest first,
    // live; the Home tile counts the ones you haven't seen.
    public sealed partial class GameUi
    {
        private const string NewsSeenKey = "fv_news_seen";

        private static readonly bool RegNews = Register("news", UiLayer.Popup, (ui, c) => ui.NewsPopup(c), .55f);

        /// <summary>The News feed, handed over by the composition root; null when the island runs on its own.</summary>
        public static NewsService? News { get; set; }

        /// <summary>Coupon codes from the server (set by GameRoot).</summary>
        public static CouponsService? Coupons { get; set; }

        /// <summary>The public profile on the server (set by GameRoot).</summary>
        public static ProfileService? Profile { get; set; }

        /// <summary>Visiting other islands (set by GameRoot: the server calls).</summary>
        public static Farmverse.Application.Features.ServerCalls? Visits { get; set; }

        /// <summary>The inbox (set by GameRoot).</summary>
        public static InboxService? Inbox { get; set; }

        /// <summary>The weekly leaderboard (set by GameRoot).</summary>
        public static LeaderboardService? Board { get; set; }

        /// <summary>News published since the player last opened News (the Home badge).</summary>
        public static int NewsUnread
        {
            get
            {
                var news = News;
                if (news == null) return 0;
                var seen = long.TryParse(PlayerPrefs.GetString(NewsSeenKey, "0"), out var s) ? s : 0;
                return news.Items.Count(n => n.PublishedAt > seen);
            }
        }

        private void NewsPopup(Ctx c)
        {
            var p = c.P;
            var news = News;
            c.Watch(() => news?.Version.Value ?? -1);
            var pop = PK.Popup(c.Root, p, 900, 820, "News", Back);
            var body = PK.Col(pop, p.P(16));
            body.style.flexGrow = 1;
            PK.Pad(body, p.P(6), p.P(20), p.P(6), 0);

            if (news == null || !news.Loaded || news.Items.Count == 0)
            {
                var empty = PK.Centre(PK.Col(body, p.P(20)));
                empty.style.flexGrow = 1;
                PK.Img(empty, "Icon_MenuIcon04_News", p.P(120));
                HomeMsg(empty, p, news == null ? "News needs the full game.<br>Start from the <b>Boot</b> scene to sign in."
                    : !news.Loaded ? "Fetching the latest news…" : "No news right now.<br>Check back soon!", 24);
                return;
            }

            // everything here is now seen
            PlayerPrefs.SetString(NewsSeenKey, news.Items.Max(n => n.PublishedAt).ToString());
            PlayerPrefs.Save();

            var sv = ShopScroller(body, false, p.P(12));
            foreach (var item in news.Items)
            {
                var card = PK.Row(sv.contentContainer, p.P(18));
                card.style.flexShrink = 0;
                card.style.alignItems = Align.FlexStart;
                card.style.marginBottom = p.P(14);
                PK.Pad(card, p.P(18), p.P(16), p.P(20), p.P(16));
                card.style.backgroundColor = GameTheme.T("#041634", .45f);
                Kit.Round(card, p.P(18));

                var art = item.Art != null && GameArt.Get(item.Art) != null ? item.Art : "Icon_Megaphone";
                PK.Img(card, art, p.P(76));
                var col = PK.Col(card, p.P(6));
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                var (title, text) = item.In(Loc.Iso);
                PK.T(col, p, title, 26);
                var date = DateTimeOffset.FromUnixTimeMilliseconds(item.PublishedAt).ToLocalTime().ToString("d MMM yyyy");
                PK.N(col, p, date, 15, GameTheme.T("#9fd0ff"));
                PK.N(col, p, text, 19, GameTheme.T("#e6f2ff"), wrap: true);
                if (!string.IsNullOrEmpty(item.Url))
                {
                    var url = item.Url!;
                    var more = PK.Btn(col, p, "blue", "Read more", 200, 58, 22, () => UnityEngine.Application.OpenURL(url));
                    more.style.alignSelf = Align.FlexStart;
                    more.style.marginTop = p.P(6);
                }
            }
        }
    }
}
