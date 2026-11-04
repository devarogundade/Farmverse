#nullable enable
using System;
using System.Linq;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The pack's Popup_Setting and Popup_Setting_Language, measured off the screenshots (2560×1440 units).
    public sealed partial class GameUi
    {
        private static readonly bool RegSettings = Register("settings", UiLayer.Popup, (ui, c) => ui.SettingsPopup(c));
        private static readonly bool RegLanguage = Register("language", UiLayer.Popup, (ui, c) => ui.LanguagePopup(c));

        private const string PushPref = "fv_push", HapticPref = "fv_haptic", LangPref = "fv_lang";

        public static readonly string[] Flags = { "Eng", "Kor", "Chn", "Rus", "Deu", "Twn", "Esp", "Jpn", "Prt", "Gre", "Swe", "Ukr", "Fra", "Ita", "Tur", "Pol", "Pra", "Tha", "Rou", "Ina" };

        public static readonly string[] Languages = { "English", "Korean", "Chinese", "Russian", "German", "Taiwanese", "Spanish", "Japanese", "Portuguese", "Greek", "Swedish", "Ukrainian", "French", "Italian", "Turkish", "Polish", "Dutch", "Thai", "Romanian", "Indonesian" };

        /// <summary>The language the player picked (a flag code); the game's text is English for now.</summary>
        public static string Language
        {
            get
            {
                // first launch: the phone's language when the game has it, else English
                var flag = PlayerPrefs.GetString(LangPref, DeviceFlag());
                return Loc.Has(IsoOfFlag(flag)) ? flag : "Eng";   // a language without text yet falls back to English
            }
        }

        private static string DeviceFlag() => UnityEngine.Application.systemLanguage switch
        {
            SystemLanguage.French => "Fra",
            SystemLanguage.Japanese => "Jpn",
            _ => "Eng",
        };

        /// <summary>The game's languages (by flag) as ISO codes, for the server (leaderboard Country tab, push copy).</summary>
        private static readonly string[] FlagIso = { "en", "ko", "zh", "ru", "de", "zh-TW", "es", "ja", "pt", "el", "sv", "uk", "fr", "it", "tr", "pl", "nl", "th", "ro", "id" };

        /// <summary>The ISO code of the chosen language.</summary>
        public static string LanguageIso => IsoOfFlag(Language);

        public static string IsoOfFlag(string flag)
        {
            var i = Array.IndexOf(Flags, flag);
            return i >= 0 ? FlagIso[i] : "en";
        }

        /// <summary>The flag for an ISO language code (a leaderboard row).</summary>
        public static string FlagOfIso(string iso)
        {
            var i = Array.IndexOf(FlagIso, iso);
            if (i < 0) i = Array.IndexOf(FlagIso, iso.Split('-', '_')[0]);
            return i >= 0 ? Flags[i] : "Eng";
        }

        /// <summary>Vibration on (the settings switch).</summary>
        public static bool Haptics => PlayerPrefs.GetInt(HapticPref, 1) == 1;

        private void SettingsPopup(Ctx c)
        {
            var r = c.R;
            var body = Kit.Popup(c.Root, r, 2200, 1000, "Settings", Back);

            // left: four rows, each an icon, a name and a switch or a slider, with a line under the first three
            var musicNow = IslandAudio.MusicOn ? IslandAudio.MusicVolume : 0f;
            VisualElement? musicIcon = null;
            for (var i = 0; i < 4; i++)
            {
                var yc = 271 + i * 190;
                var icon = i switch
                {
                    0 => "Icon_PictoIcon_Bell",
                    1 => "Icon_PictoIcon_Sound_on",
                    2 => musicNow > 0 ? "Icon_PictoIcon_Music_on" : "Icon_PictoIcon_Music_off",
                    _ => "Icon_PictoIcon_Haptic",
                };
                var ic = Kit.Image(body, r, icon, 139 - 36, yc - 36, 72, 72);
                if (i == 2) musicIcon = ic;
                Kit.TextAt(body, r, 210, yc - 30, i switch { 0 => "Push Alarm", 1 => "Sound Fx", 2 => "Music", _ => "Vibration" }, 52);

                switch (i)
                {
                    case 0:
                        Kit.Switch(body, r, 826, yc, PlayerPrefs.GetInt(PushPref, 0) == 1, on => SettingsPush(on));
                        // which pushes: raids, island, rewards, offers
                        Kit.Button(body, r, "Blue", 560, yc - 42, 150, 84, () => Open("pushtopics"), "Edit", 40);
                        break;
                    case 1:
                        Kit.TickSlider(body, r, 657, yc, IslandAudio.Muted ? 0f : IslandAudio.SoundVolume, v =>
                        {
                            IslandAudio.SoundVolume = v;
                            if (v > 0 && IslandAudio.Muted) IslandAudio.Muted = false;
                        }, _ => IslandAudio.Play("pop"));
                        break;
                    case 2:
                        Kit.TickSlider(body, r, 657, yc, musicNow, v =>
                        {
                            IslandAudio.MusicVolume = v;
                            IslandAudio.MusicOn = v > 0;
                            if (musicIcon != null) Kit.Contain(musicIcon, GameArt.Get(v > 0 ? "Icon_PictoIcon_Music_on" : "Icon_PictoIcon_Music_off"));
                        });
                        break;
                    default:
                        Kit.Switch(body, r, 826, yc, Haptics, on =>
                        {
                            Pref(HapticPref, on);
#if UNITY_ANDROID || UNITY_IOS
                            if (on) Handheld.Vibrate();
#endif
                        });
                        break;
                }

                if (i < 3)
                {
                    var line = Kit.Box(body, r, 94, yc + 92, 900, 4);
                    Kit.Colour(line, GameTheme.T("#104f8f"));
                }
            }

            // right: two columns of blue buttons
            VisualElement B(int col, int row, string colour, string? label, Action click) =>
                Kit.Button(body, r, colour, col == 1 ? 1638 : 1088, 204 + row * 190, 466, 132, click, label);

            var lang = Array.IndexOf(Flags, Language);
            if (lang < 0) lang = 0;
            var langBtn = B(0, 0, "Blue", null, () => Open("language"));
            Kit.TextAt(langBtn, r, 50, 30, Languages[lang], 54);
            var flag = Kit.Box(langBtn, r, 466 - 16 - 104, 10, 104, 104);
            Kit.Contain(flag, GameArt.Get("Icon_Flag_" + Flags[lang]));

            B(1, 0, "Blue", "Profile", () => Open("profile"));
            B(0, 1, "Blue", "Save Farm", () => Open("savefarm"));
            B(1, 1, "Blue", "Rate", () => Open("rate"));
            B(0, 2, "Blue", "Theme", () => Open("theme"));
            B(1, 2, "Blue", "Support", () => GameLinks.EmailSupport(Profile?.Player?.ShortId ?? ""));
            B(0, 3, "BlueGray", "Delete Account", AccountDeleteAsk);
            B(1, 3, "Blue", "Privacy", () =>
            {
                // the ad consent choices where the law asks for them, else the privacy policy
                if (Ads is { PrivacyOptionsRequired: true } ads) ads.ShowPrivacyOptions();
                else GameLinks.Open(GameLinks.Privacy);
            });
        }

        /// <summary>The push switch: on asks for permission and opts in on the server; off opts out.</summary>
        private async void SettingsPush(bool on)
        {
            Pref(PushPref, on);
            if (on && LocalNotifications is { } local) await local.RequestPermission();
            var n = Notifications;
            if (n == null) return;
            var result = await n.SetPushEnabled(on);
            if (result.IsOk) return;
            Pref(PushPref, false);
            Say(on ? "Notifications are off for Farmverse in your phone's settings" : "Couldn't change it — check your connection");
            MarkDirty();
        }

        private void LanguagePopup(Ctx c)
        {
            var r = c.R;
            var body = Kit.Popup(c.Root, r, 1480, 1100, "LANGUAGE", Back);
            var current = Language;
            // only the languages the game has text for (Resources/Localization/{iso}.json; English always)
            var shown = Flags.Where(f => Loc.Has(IsoOfFlag(f))).ToArray();
            for (var i = 0; i < shown.Length; i++)
            {
                var code = shown[i];
                var cell = Kit.Box(body, r, 105 + i % 5 * 277, 205 + i / 5 * 229, 160, 160);
                var shadow = Kit.Box(cell, r, 0, 8, 160, 160);   // the flag's drop shadow
                Kit.Colour(shadow, GameTheme.Shade(.45f));
                Kit.Round(shadow, r.Q(10));
                var f = Kit.Fill(cell, r);
                Kit.Cover(f, GameArt.Get("Icon_Flag_" + code));
                Kit.Round(f, r.Q(10));
                Kit.Border(f, r.Q(3), Color.black);
                if (code == current) Kit.Image(cell, r, "Toggle01_Check_Green", 100, 80, 102, 102 * 114f / 102f);
                Kit.Press(cell, r, () =>
                {
                    PlayerPrefs.SetString(LangPref, code);
                    Loc.Use(IsoOfFlag(code));
                    _ = Notifications?.SetLocale(IsoOfFlag(code));
                    if (Profile is { Online: true } prof) _ = prof.SetLocale(IsoOfFlag(code));
                    PlayerPrefs.Save();
                    Back();
                    MarkDirty();
                });
            }
        }

        private static void Pref(string key, bool on)
        {
            PlayerPrefs.SetInt(key, on ? 1 : 0);
            PlayerPrefs.Save();
        }

    }
}
