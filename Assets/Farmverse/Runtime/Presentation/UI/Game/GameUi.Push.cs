#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Farmverse.Application.Ports;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// Notifications, the player's side: asking for permission at a moment it makes sense (right after
    /// their first raid, or on a later day of play — never on first launch), at most twice, three days
    /// apart; and Settings → Notifications, one switch per topic (raids, island, rewards, offers).
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegPush_Ask = Register("pushask", UiLayer.Popup, (ui, c) => ui.PushAsk(c), .55f);
        private static readonly bool RegPush_Topics = Register("pushtopics", UiLayer.Popup, (ui, c) => ui.PushTopics(c), .55f);

        /// <summary>The phone's own reminders (set by GameRoot): asked for permission with the push.</summary>
        public static ILocalNotifications? LocalNotifications { get; set; }

        private const string AskCountPref = "fv_push_asks", AskAtPref = "fv_push_ask_at", FirstDayPref = "fv_first_day";
        private const int MaxAsks = 2;
        private const double AskGapDays = 3;

        /// <summary>The switches, as the server knows them (PushTopic) with what each covers.</summary>
        private static readonly (string topic, string name, string about, string icon)[] PushTopicRows =
        {
            ("raids", "Raids", "Warnings before the zombies come, and how the night went", "Icon_Sword"),
            ("island", "Island", "Full storage, likes, crops and workshops done", "G_Wheat"),
            ("rewards", "Rewards", "Daily gifts, free coins and welcome-back gifts", "ResourceBar_Icon_Gold"),
            ("offers", "Offers", "Shop deals about to end", "Icon_Shield"),
            ("news", "News", "Big updates and events on Farmverse", "Icon_Megaphone"),
        };

        public static bool PushTopicOn(string topic) => PlayerPrefs.GetInt("fv_push_" + topic, 1) == 1;

        private int _pushAskRaids = -1;

        /// <summary>Every frame: is this a good moment to ask for notifications?</summary>
        private void PushAskTick()
        {
            if (BaseScreen != "play" || TopPopup != null || Notifications == null) return;
            if (PlayerPrefs.GetInt(PushPref, 0) == 1 || PlayerPrefs.GetInt(AskCountPref, 0) >= MaxAsks) return;
            var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!PlayerPrefs.HasKey(FirstDayPref)) PlayerPrefs.SetString(FirstDayPref, today);
            var last = PlayerPrefs.GetString(AskAtPref, "");
            if (last.Length > 0 && DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) && (DateTime.UtcNow - at).TotalDays < AskGapDays) return;

            var life = Life;
            if (life == null) return;
            if (_pushAskRaids < 0) _pushAskRaids = life.RaidsEnded;
            // right after a raid's result was closed: the warning is the obvious value
            var afterRaid = life.RaidsEnded > _pushAskRaids;
            // or a few minutes into a session on a later day
            var laterDay = PlayerPrefs.GetString(FirstDayPref, today) != today && Time.unscaledTime > 240f;
            if (!afterRaid && !laterDay) return;
            _pushAskRaids = life.RaidsEnded;
            PlayerPrefs.SetInt(AskCountPref, PlayerPrefs.GetInt(AskCountPref, 0) + 1);
            PlayerPrefs.SetString(AskAtPref, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            Open("pushask");
        }

        private void PushAsk(Ctx c)
        {
            var p = c.P;
            var pop = PK.Popup(c.Root, p, 760, 0, "Never miss a raid", Back, "Blue");
            var col = PK.Col(pop, p.P(16));
            col.style.alignItems = Align.Center;
            PK.Pad(col, 0, p.P(20), 0, p.P(6));
            PK.Img(col, "Icon_PictoIcon_Bell", p.P(110));
            var text = PK.N(col, p, "We'll warn you before the zombies come, and tell you when crops are ready or a gift is waiting.", 20, GameTheme.C("#dff1ff"));
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.maxWidth = p.P(640);
            PK.N(col, p, "Only a few a day, never at night. Change it any time in Settings.", 16, GameTheme.T("#9fd0ff"));
            var buttons = PK.Row(col, p.P(16));
            PK.Btn(buttons, p, "gray", "Not now", 200, 68, 24, Back);
            PK.Btn(buttons, p, "green", "Turn on", 230, 68, 26, () =>
            {
                Back();
                TurnOnPush();
            });
        }

        /// <summary>Ask the OS (push and the phone's own reminders), register, opt in on the server.</summary>
        private async void TurnOnPush()
        {
            Pref(PushPref, true);
            var local = LocalNotifications;
            if (local != null) await local.RequestPermission();
            var n = Notifications;
            if (n == null) return;
            var result = await n.SetPushEnabled(true);
            if (result.IsOk)
            {
                Say("Notifications on: we'll warn you before raids");
                return;
            }
            Pref(PushPref, false);
            Say("Notifications are off for Farmverse in your phone's settings");
        }

        private void PushTopics(Ctx c)
        {
            var p = c.P;
            var pop = PK.Popup(c.Root, p, 820, 0, "Notifications", Back, "Blue");
            var col = PK.Col(pop, p.P(12));
            PK.Pad(col, p.P(10), p.P(16), p.P(10), p.P(6));
            foreach (var (topic, name, about, icon) in PushTopicRows)
            {
                var row = PK.Row(col, p.P(14));
                row.style.alignItems = Align.Center;
                row.style.backgroundColor = PK.Ink;
                Kit.Round(row, p.U(14));
                PK.Pad(row, p.P(16), p.P(10), p.P(16), p.P(10));
                PK.Img(row, icon, p.P(54));
                var words = PK.Col(row, p.P(2));
                words.style.flexGrow = 1;
                words.style.flexShrink = 1;
                PK.T(words, p, name, 24);
                var a = PK.N(words, p, about, 15, GameTheme.T("#9fd0ff"));
                a.style.whiteSpace = WhiteSpace.Normal;
                var t = topic;
                PK.Switch(row, p, PushTopicOn(t), on => SetPushTopic(t, on));
            }
            var note = PK.N(col, p, "Never while you're playing, never at night, a few a day at most.", 15, GameTheme.T("#9fd0ff"));
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            var buttons = PK.Row(col, p.P(16));
            buttons.style.justifyContent = Justify.Center;
            PK.Btn(buttons, p, "blue", "OK", 190, 68, 26, Back);
        }

        private async void SetPushTopic(string topic, bool on)
        {
            PlayerPrefs.SetInt("fv_push_" + topic, on ? 1 : 0);
            PlayerPrefs.Save();
            var n = Notifications;
            if (n == null) return;
            var all = new Dictionary<string, bool>();
            foreach (var row in PushTopicRows) all[row.topic] = PushTopicOn(row.topic);
            var result = await n.SetPushTopics(all);
            if (!result.IsOk) Say("Couldn't save it — check your connection");
        }
    }
}
