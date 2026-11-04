#nullable enable
using System.Globalization;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The pack's Popup_UserInfo and Popup_ChangeName, and the theme picker, in 2560×1440 units.
    public sealed partial class GameUi
    {
        private static readonly bool RegTheme = Register("theme", UiLayer.Popup, (ui, c) => ui.ThemePopup(c));
        private static readonly bool RegProfile = Register("profile", UiLayer.Popup, (ui, c) => ui.ProfilePopup(c));
        private static readonly bool RegName = Register("name", UiLayer.Popup, (ui, c) => ui.NamePopup(c));

        private const string NamePref = "fv_player_name";

        /// <summary>The player's name (the profile; free to change for now).</summary>
        public static string PlayerName => PlayerPrefs.GetString(NamePref, "Farmer");

        private static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>A farm's title by its net worth (the catalogue's profile.farmTitles).</summary>
        private static string FarmTitle(long worth)
        {
            var title = "Settler";
            foreach (var t in Wallet.Prices.Profile.FarmTitles)
                if (worth >= t.MinWorth) title = t.Title;
            return title;
        }

        // ------------------------------------------------------------------ theme

        private static readonly (string id, string name, string c1, string c2, string c3)[] ThemeCards =
        {
            ("meadow", "Meadow", "#2f9a2a", "#1d6b1a", "#5be04a"),
            ("harvest", "Harvest", "#a8642a", "#6e3d14", "#d8873c"),
            ("orchard", "Orchard", "#1f8f78", "#125c4e", "#2fc2a0"),
            ("mix", "Mix", "#a8642a", "#6e3d14", "#5be04a"),
        };

        private void ThemePopup(Ctx c)
        {
            var r = c.R;
            var body = Kit.Popup(c.Root, r, 1540, 700, "Theme", Back);
            for (var i = 0; i < ThemeCards.Length; i++)
            {
                var (id, label, c1, c2, c3) = ThemeCards[i];
                var on = id == GameTheme.Current;
                var card = Kit.Box(body, r, 110 + i * 330, 210, 290, 360);

                if (on)
                {
                    var ring = Kit.Fill(card, r, -8, -8, -8, -8);   // the gold ring 8 outside the black edge
                    Kit.Colour(ring, GameTheme.C("#ffd23a"));
                    Kit.Round(ring, r.Q(26));
                }

                var shadow = Kit.Fill(card, r, 0, 10, 0, -10);
                Kit.Colour(shadow, GameTheme.Shade(.45f));
                Kit.Round(shadow, r.Q(18));

                var face = Kit.Fill(card, r);
                Kit.Picture(face, GameArt.Linear("theme_" + id, true, (0f, GameTheme.C(c1)), (1f, GameTheme.C(c2))));
                Kit.Round(face, r.Q(18));
                Kit.Border(face, r.Q(4), Color.black);

                var panel = Kit.Box(card, r, 30, 30, 230, 150);
                Kit.Colour(panel, GameTheme.C(c2));
                Kit.Round(panel, r.Q(12));
                Kit.Border(panel, r.Q(3), Color.black);

                var bar = Kit.Box(card, r, 30, 200, 230, 60);
                Kit.Colour(bar, GameTheme.C(c3));
                Kit.Round(bar, r.Q(12));
                Kit.Border(bar, r.Q(3), Color.black);
                var barShade = Kit.Fill(bar, r, 0, 60 - 6 - 8, 0, 0);
                Kit.Colour(barShade, GameTheme.Shade(.2f));
                Kit.Round(barShade, 0, 0, r.Q(9), r.Q(9));

                Kit.TextAt(card, r, 0, 285, label, 48, width: 290, align: TextAnchor.MiddleCenter);
                if (on) Kit.Image(card, r, "Toggle01_Check_Green", 290 + 20 - 90, -24, 90, 90 * 114f / 102f);

                Kit.Press(card, r, () => GameTheme.Current = id);
            }
        }

        // ------------------------------------------------------------------ profile

        private void ProfilePopup(Ctx c)
        {
            var r = c.R;
            const float w = 1640;
            var body = Kit.Popup(c.Root, r, w, 1080, null, Back);
            var eco = Host != null ? Host.Economy : null;

            // header: the lighter band with a line through it, a black line under it
            var band = Kit.Box(body, r, 4, 4, w - 8, 438);
            Kit.Picture(band, GameArt.Linear("profband_" + GameTheme.Current, false, (0f, GameTheme.T("#055a9f")), (1f, GameTheme.T("#0868b3"))));
            Kit.Round(band, r.Q(10), r.Q(10), 0, 0);
            Kit.Colour(Kit.Box(body, r, 4, 245, w - 8, 4), GameTheme.T("#00143c", .35f));
            Kit.Colour(Kit.Box(body, r, 4, 438, w - 8, 4), Color.black);

            // avatar in the pack's profile frame, half out of the popup
            var ava = Kit.Box(body, r, 60, -60, 240, 240);
            Kit.Picture(ava, GameArt.Radial("ava_" + GameTheme.Current, .5f, .5f, .5f, .5f, (0f, GameTheme.T("#5fc0ff")), (.999f, GameTheme.T("#1676d1")), (1f, GameTheme.T("#1676d1", 0f))));
            Kit.Image(ava, r, "ProfileFrame01_Border", -14.4f, -14.4f, 268.8f, 268.8f, contain: false);
            Kit.Image(ava, r, HomeAvatarNow, 45.6f, 45.6f, 148.8f, 148.8f);
            Kit.Press(ava, r, () => Open("avatar"));

            // name, edit, player id
            var nameBox = Kit.Box(body, r, 335, 67, 640, 122);
            Kit.Colour(nameBox, GameTheme.T("#062a55"));
            Kit.Border(nameBox, r.Q(3), Color.black);
            Kit.Round(nameBox, r.Q(14));
            Kit.TextAt(nameBox, r, 34, 32, PlayerName, 62);
            var edit = Kit.Button(body, r, "Blue", 880, 78, 110, 96, () => Open("name"));
            Kit.Image(edit, r, "Icon_PictoIcon_Edit", (110 - 52.3f) / 2f, (96 - 8 - 56) / 2f, 52.3f, 56);

            var light = GameTheme.T("#6fc0ff");
            Kit.TextAt(body, r, 1050, 85, "PLAYER ID", 34, light, false);
            var id = Profile?.Player?.ShortId ?? "";
            Kit.TextAt(body, r, 1050, 128, string.IsNullOrEmpty(id) ? "—" : "#" + id.ToUpperInvariant(), 62);

            // level and xp
            var level = eco?.IslandLevel ?? 1;
            long xpIn = 0, xpNeed = GameplayRules.XpToNext(level);
            if (eco != null) xpIn = eco.Xp - GameplayRules.XpAt(level);
            var bar = Kit.Box(body, r, 170, 310, 560, 72);
            Kit.Colour(bar, GameTheme.T("#1d2040"));
            Kit.Border(bar, r.Q(3), Color.black);
            Kit.Round(bar, r.Q(10));
            bar.style.overflow = Overflow.Hidden;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.width = new Length(Mathf.Clamp01(xpNeed > 0 ? xpIn / (float)xpNeed : 0f) * 100f, LengthUnit.Percent);
            Kit.Picture(fill, GameArt.Linear("xpfill_" + GameTheme.Current, true, (0f, GameTheme.T("#36c8ff")), (1f, GameTheme.T("#1793e8"))));
            bar.Add(fill);
            Kit.TextIn(bar, r, N(xpIn) + "/" + N(xpNeed), 44);
            var badge = Kit.Image(body, r, "Slider_Level01_Badge_Blue", 90, 296, 110, 122);
            Kit.TextIn(badge, r, level.ToString(CultureInfo.InvariantCulture), 56);

            // farm title
            var worth = eco?.NetWorth ?? 0;
            Kit.Image(body, r, "Icon_Laurel", 860, 282, 124, 124);
            Kit.TextAt(body, r, 1015, 296, FarmTitle(worth), 52);
            Kit.TextAt(body, r, 1015, 354, "FARM TITLE", 34, GameTheme.T("#34e4ff"), false);

            // stats
            Kit.TextAt(body, r, 80, 515, "FARM STATS", 34, light, false);
            float[] colX = { 80, 585, 1086 };
            const float tw = 475;
            var big = new (string icon, string label, string value)[]
            {
                ("ResourceBar_Icon_Gold", "Net Worth", N(worth)),
                ("Icon_ImageIcon_Map_l", "Orders Filled", N(eco?.Stat("orders") ?? 0)),
                ("Icon_ImageIcon_Medal_Silver", "World Rank", LbMyRank("world")),
            };
            for (var k = 0; k < big.Length; k++)
            {
                var tile = Kit.Box(body, r, colX[k], 594, tw, 124);
                Kit.Colour(tile, GameTheme.T("#0d2650"));
                Kit.Border(tile, r.Q(3), Color.black);
                Kit.Round(tile, r.Q(12));
                tile.style.overflow = Overflow.Hidden;
                var cell = Kit.Box(tile, r, -3, -3, 150, 124);
                Kit.Picture(cell, GameArt.Slant("statcell_" + GameTheme.Current, GameTheme.T("#0a1e40"), .85f));
                Kit.Image(cell, r, big[k].icon, 15, 14, 120, 96);
                Kit.TextAt(tile, r, 149, 13, big[k].label, 34, light, false);
                Kit.TextAt(tile, r, 149, 53, big[k].value, 58);
            }

            // days since the account was made (the server's player doc)
            var days = 1;
            var created = Profile?.Player?.CreatedAt ?? 0;
            if (created > 0) days = Mathf.Max(1, (int)((World.Island.IslandEconomyHost.Clock - created / 1000.0) / 86400.0) + 1);
            var small = new (string icon, string label, string value)[]
            {
                ("Icon_Pumkin", "Crops Grown", N(eco?.Stat("harvested") ?? 0)),
                ("Icon_GearWheels", "Goods Sold", N(eco?.Stat("sold") ?? 0)),
                ("Icon_Shield", "Raids Beaten", N(eco?.Stat("raids_won") ?? 0)),
                ("Icon_Hammer", "Upgrades", N((eco?.Stat("upgrades") ?? 0) + (eco?.Stat("building_upgrades") ?? 0))),
                ("G_Calendar", "Days Played", N(days)),
                ("Icon_Star", "Goals Done", N(eco?.GoalIndex ?? 0)),
            };
            for (var k = 0; k < small.Length; k++)
            {
                var tile = Kit.Box(body, r, colX[k % 3], 766 + k / 3 * 132, tw, 90);
                Kit.Colour(tile, GameTheme.T("#0d2650"));
                Kit.Round(tile, r.Q(10));
                Kit.Image(tile, r, small[k].icon, 14, 10, 70, 70);
                Kit.TextAt(tile, r, 96, -22, small[k].label, 32, light, false);
                Kit.TextAt(tile, r, 96, 20, small[k].value, 56);
            }
        }

        // ------------------------------------------------------------------ change name

        private void NamePopup(Ctx c)
        {
            var r = c.R;
            const float w = 1093;
            var body = Kit.Popup(c.Root, r, w, 720, "Change Name", Back);
            Kit.TextRow(body, r, 206, "Please enter a name to change.", 38, GameTheme.T("#9fcfff"), false);

            var box = Kit.Box(body, r, 136, 282, w - 272, 126);
            Kit.Colour(box, Color.white);
            Kit.Border(box, r.Q(3), Color.black);
            Kit.Round(box, r.Q(16));
            box.style.overflow = Overflow.Hidden;
            Kit.Colour(Kit.Box(box, r, 0, 0, w - 272, 10), GameTheme.T("#8ed6f5"));   // the inset band at the top

            var field = new TextField { value = PlayerName, maxLength = 16 };
            field.style.position = Position.Absolute;
            field.style.left = 0;
            field.style.right = 0;
            field.style.top = r.Q(6);
            field.style.bottom = 0;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.marginTop = 0;
            field.style.marginBottom = 0;
            var font = GameArt.Display;
            var input = field.Q(className: TextField.inputUssClassName) ?? field;
            foreach (var e in new[] { field, input, input.Q<TextElement>() })
            {
                if (e == null) continue;
                if (font != null) e.style.unityFontDefinition = FontDefinition.FromFont(font);
                e.style.fontSize = r.Q(48);
                e.style.color = Color.black;
                e.style.unityTextAlign = TextAnchor.MiddleCenter;
                e.style.backgroundColor = Color.clear;
                Kit.Border(e, 0, Color.clear);
                e.style.paddingLeft = 0;
                e.style.paddingRight = 0;
            }

            input.style.flexGrow = 1;
            box.Add(field);

            var error = Kit.TextRow(body, r, 430, "", 30, GameTheme.C("#ff7e93"), false);

            void Submit()
            {
                var name = (field.value ?? "").Trim();
                string? problem = null;
                if (name.Length < 3) problem = "! Use at least 3 letters.";
                else
                    foreach (var ch in name)
                        if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == ' ')) { problem = "! Letters, numbers, spaces and _ only."; break; }
                if (problem != null)
                {
                    error.text = Loc.Of(problem);
                    World.Island.IslandAudio.Play("error");
                    return;
                }

                ProfileRename(name);
                Back();
            }

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit();
            }, TrickleDown.TrickleDown);
            Kit.Button(body, r, "Green", (w - 420) / 2f, 500, 420, 130, Submit, "Save");
            field.schedule.Execute(() => field.Focus());
        }

        /// <summary>Rename on the server (names are unique there); the device keeps the name once the server agrees.</summary>
        private async void ProfileRename(string name)
        {
            var prof = Profile;
            if (prof == null || !prof.Online)
            {
                Say("Connect to the internet to change your name");
                return;
            }
            var result = await prof.Rename(name);
            if (result.IsFailure)
            {
                Say(result.Error.Code == Farmverse.Contracts.ErrorCode.NameTaken ? "That name is taken — try another" : "Couldn't change your name — check your connection");
                return;
            }
            PlayerPrefs.SetString(NamePref, name);
            PlayerPrefs.Save();
            Say("Name changed!");
            MarkDirty();
        }

        private bool _profileSynced;

        /// <summary>
        /// Every frame until done once a session: line the device's profile up with the server's — the server's
        /// name wins (it is unique), the device's avatar and language go up.
        /// </summary>
        private void ProfileTick()
        {
            if (_profileSynced) return;
            var prof = Profile;
            var server = prof?.Player;
            if (prof == null || server == null) return;
            _profileSynced = true;
            if (PlayerPrefs.GetString(NamePref, "") != server.DisplayName)
            {
                PlayerPrefs.SetString(NamePref, server.DisplayName);
                PlayerPrefs.Save();
                MarkDirty();
            }
            if (PlayerPrefs.HasKey("fv_avatar"))
            {
                var mine = AvatarIdOf(HomeAvatarNow);
                if (mine != server.AvatarId) _ = prof.SetAvatar(mine);
            }
            else if (server.AvatarId != Farmverse.Application.Session.SessionService.DefaultAvatar)
            {
                PlayerPrefs.SetString("fv_avatar", AvatarArtOf(server.AvatarId));
                PlayerPrefs.Save();
            }
            _ = prof.SetLocale(LanguageIso);
        }
    }
}
