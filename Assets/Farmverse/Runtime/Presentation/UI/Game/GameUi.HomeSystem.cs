#nullable enable
using System;
using Farmverse.Application.Ports;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>What the "confirm" popup asks: title, message, the OK button, and optionally a good → coins line.</summary>
    /// <summary>What the message dialog shows (<see cref="GameUi.Tell"/>).</summary>
    public sealed class HomeMessageArgs
    {
        public string Title = "";
        /// <summary>The message; <c>&lt;br&gt;</c> breaks a line, <c>&lt;b&gt;</c> highlights.</summary>
        public string Text = "";
    }

    public sealed class HomeConfirmArgs
    {
        public string Title = "Are you sure?";
        public string Text = "";
        public string Ok = "OK";
        public string Cancel = "Cancel";
        public Action? OnOk;
        /// <summary>Optional: the item picture and count on the left, coins on the right (selling).</summary>
        public string? Icon;
        public string? Count;
        public string? Coins;
    }

    /// <summary>
    /// The small shared popups to system.html (Terms, Update / Update required, Maintenance, No connection,
    /// Confirm, Remove ads, Please wait) and the profile extras to profile.html (Choose avatar, Save your
    /// farm, Rate us). One frame (Popup03_Single), short text, green = yes, blue/gray = no.
    /// </summary>
    public sealed partial class GameUi
    {
        private static readonly bool RegHome_Terms = Register("terms", UiLayer.Popup, (ui, c) => ui.HomeTerms(c));
        private static readonly bool RegHome_Update = Register("update", UiLayer.Popup, (ui, c) => ui.HomeUpdate(c));
        private static readonly bool RegHome_Maint = Register("maintenance", UiLayer.Popup, (ui, c) => ui.HomeSimple(c, "Under maintenance", "We're improving the island.<br>Your farm is safe — please try again in a little while.", new[] { ("blue", "OK", (Action)ui.Back), ("green", "Retry", (Action)ui.SystemRetry) }));
        private static readonly bool RegHome_Net = Register("noconnection", UiLayer.Popup, (ui, c) => ui.HomeNoConnection(c));
        private static readonly bool RegHome_Confirm = Register("confirm", UiLayer.Popup, (ui, c) => ui.HomeConfirm(c));
        private static readonly bool RegHome_Message = Register("message", UiLayer.Popup, (ui, c) =>
        {
            var a = c.Args as HomeMessageArgs ?? new HomeMessageArgs();
            ui.HomeSimple(c, a.Title.Length > 0 ? a.Title : "Note", a.Text, new[] { ("green", "OK", (Action)ui.Back) });
        });
        private static readonly bool RegHome_NoAds = Register("removeads", UiLayer.Popup, (ui, c) => ui.HomeRemoveAds(c));
        private static readonly bool RegHome_Wait = Register("wait", UiLayer.Popup, (ui, c) => ui.HomeWait(c));
        private static readonly bool RegHome_Avatar = Register("avatar", UiLayer.Popup, (ui, c) => ui.HomeAvatar(c), .5f);
        private static readonly bool RegHome_Save = Register("savefarm", UiLayer.Popup, (ui, c) => ui.HomeSaveFarm(c), .5f);
        private static readonly bool RegHome_Rate = Register("rate", UiLayer.Popup, (ui, c) => ui.HomeRate(c), .5f);

        /// <summary>The avatars the player can pick (the last two unlock at the catalogue's island level).</summary>
        public static readonly string[] HomeAvatars = { "Icon_Pumkin", "Icon_Egg", "Icon_Clover", "Icon_Horsesheos", "Icon_Heart", "Icon_Star", "Icon_Food_Meat", "Icon_Gift", "Icon_Shield", "Icon_GearWheels" };

        /// <summary>The server id of an avatar picture ("Icon_Pumkin" → "avatar_icon_pumkin").</summary>
        public static string AvatarIdOf(string art)
        {
            var sb = new System.Text.StringBuilder("avatar_");
            var under = false;
            foreach (var ch in art.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); under = false; }
                else if (!under) { sb.Append('_'); under = true; }
            }
            return sb.ToString().TrimEnd('_');
        }

        /// <summary>The picture for a server avatar id (unknown ids and "avatar_default" get the first one).</summary>
        public static string AvatarArtOf(string avatarId)
        {
            foreach (var a in HomeAvatars)
                if (AvatarIdOf(a) == avatarId) return a;
            return HomeAvatars[0];
        }

        /// <summary>The player's avatar (PlayerPrefs fv_avatar; mirrored to the server profile).</summary>
        public static string HomeAvatarNow => PlayerPrefs.GetString("fv_avatar", "Icon_Pumkin");

        private string? _homeAvaPick;

        // ------------------------------------------------------------------ shared

        /// <summary>The kit's simple popup: title band, optional icon, message, one or two buttons (close only with two).</summary>
        private void HomeSimple(Ctx c, string title, string msg, (string colour, string label, Action go)[] buttons, float w = 600, string? icon = null)
        {
            var p = c.P;
            var pop = PK.Popup(c.Root, p, w, 0, title, buttons.Length > 1 ? Back : (Action?)null);
            var body = PK.Col(pop, p.P(20));
            body.style.alignItems = Align.Center;
            PK.Pad(body, p.P(10), p.P(26), p.P(10), p.P(6));
            if (icon != null) PK.Img(body, icon, p.P(110));
            HomeMsg(body, p, msg, 21);
            var row = PK.Row(body, p.P(18));
            foreach (var (colour, label, go) in buttons) PK.Btn(row, p, colour, label, 200, 70, 26, go);
        }

        // ------------------------------------------------------------------ system

        private void HomeTerms(Ctx c)
        {
            var p = c.P;
            var bg = PK.Inset(PK.Box(c.Root));
            bg.pickingMode = PickingMode.Position;
            bg.style.backgroundColor = GameTheme.T("#0d5aa4");
            Kit.Cover(bg, GameArt.Get("Home_LoadingBg"));
            var logo = PK.Box(c.Root);   // the painted logo (Home_Logo)
            PK.Abs(logo, left: (p.W - p.P(574)) / 2f, top: p.P(p.Phone ? 10 : 90), width: p.P(574), height: p.P(196));   // 70% of the mockup's logo box
            Kit.Contain(logo, GameArt.Get("Home_Logo"));

            var pop = PK.Popup(c.Root, p, 640, 0, "Before you start", null);
            var body = PK.Col(pop, p.P(18));
            body.style.alignItems = Align.Center;
            PK.Pad(body, 0, p.P(24), 0, p.P(6));
            var msg = HomeMsg(body, p, "Tap <b>OK</b> to agree to our <u>Terms of Service</u><br>and <u>Privacy Policy</u>.", 20);
            msg.enableRichText = true;
            var row = PK.Row(body, p.P(18));
            PK.Btn(row, p, "blue", "Read terms", 220, 70, 24, () => GameLinks.Open(GameLinks.Terms));
            PK.Btn(row, p, "green", "OK", 200, 70, 28, () =>
            {
                PlayerPrefs.SetInt("fv_terms_ok", 1);
                PlayerPrefs.Save();
                Back();
            });
        }

        /// <summary>Update available (args null) or Update required (args "force": no Later, no close).</summary>
        private void HomeUpdate(Ctx c)
        {
            var force = c.Args as string == "force";
            void Store() => GameLinks.Open(GameLinks.StorePage);
            if (force) HomeSimple(c, "Update required", "This version is too old to connect.<br>Please update from the store.", new[] { ("green", "Update", (Action)Store) });
            else HomeSimple(c, "Update available", "A new version of Farmverse is ready.<br>Update now to keep playing.", new[] { ("blue", "Later", (Action)Back), ("green", "Update", (Action)Store) });
        }

        private void HomeNoConnection(Ctx c)
        {
            var p = c.P;
            var band = PK.Col(c.Root, p.P(14));
            band.pickingMode = PickingMode.Position;
            band.style.position = Position.Absolute;
            band.style.left = 0;
            band.style.right = 0;
            band.style.top = new Length(30, LengthUnit.Percent);
            band.style.height = p.P(320);
            band.style.alignItems = Align.Center;
            band.style.justifyContent = Justify.Center;
            PK.Grad(band, "homeband", true, (0f, GameTheme.T("#0d2f66")), (1f, GameTheme.T("#0a2550")));
            band.style.borderTopWidth = p.U(3);
            band.style.borderBottomWidth = p.U(3);
            band.style.borderTopColor = PK.Ink;
            band.style.borderBottomColor = PK.Ink;
            PK.Img(band, "Icon_ImageIcon_NetworkError", p.P(110));
            PK.T(band, p, "Connection lost", 30, GameTheme.C("#ff7a8c"));
            HomeMsg(band, p, "Check your Wi-Fi or mobile data. Your farm is safe — we'll sync when you're back.", 18);
            var btns = PK.Row(band, p.P(18));
            PK.Btn(btns, p, "gray", "Play offline", 240, 66, 24, Back);
            PK.Btn(btns, p, "blue", "Retry", 220, 66, 26, () =>
            {
                if (UnityEngine.Application.internetReachability == NetworkReachability.NotReachable) Say("Still offline");
                else SystemRetry();
            });
        }

        private void HomeConfirm(Ctx c)
        {
            var p = c.P;
            var a = c.Args as HomeConfirmArgs ?? new HomeConfirmArgs();
            var pop = PK.Popup(c.Root, p, 600, 0, a.Title, Back);
            var body = PK.Col(pop, p.P(18));
            body.style.alignItems = Align.Center;
            PK.Pad(body, 0, p.P(24), 0, p.P(6));
            if (a.Icon != null)
            {
                var row = PK.Row(body, p.P(24));
                PK.Slot(row, p, a.Icon, a.Count, "Yellow", 120);
                PK.T(row, p, "→", 50, stroke: false);
                if (a.Coins != null)
                {
                    var col = PK.Col(row);
                    col.style.alignItems = Align.Center;
                    PK.Img(col, "ResourceBar_Icon_Gold", p.P(90));
                    PK.T(col, p, a.Coins, 34);
                }
            }

            if (a.Text.Length > 0) HomeMsg(body, p, a.Text, 17);
            var btns = PK.Row(body, p.P(18));
            PK.Btn(btns, p, "gray", a.Cancel, 200, 70, 26, Back);
            PK.Btn(btns, p, "green", a.Ok, 200, 70, 28, () =>
            {
                Back();
                a.OnOk?.Invoke();
            });
        }

        private void HomeRemoveAds(Ctx c)
        {
            var p = c.P;
            HomeDim(c.Root, "#030c20", .85f);
            var col = PK.Col(c.Root, p.P(20));
            col.style.position = Position.Absolute;
            col.style.left = new Length(50, LengthUnit.Percent);
            col.style.top = new Length(50, LengthUnit.Percent);
            col.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            col.style.alignItems = Align.Center;
            var glow = PK.Centre(PK.Box(col));
            PK.Size(glow, p.P(260), p.P(220));
            Kit.Picture(glow, GameArt.Radial("homeadglow", .5f, .5f, .5f, .5f, (0f, GameTheme.T("#3ca0ff", .45f)), (.7f, GameTheme.T("#3ca0ff", 0f)), (1f, new Color(0, 0, 0, 0))));
            PK.Img(glow, "Icon_ImageIcon_AdRemove_l", p.P(200));
            var noAdsId = Wallet.Prices.Iap.Where(kv => kv.Value.Kind == Farmverse.Contracts.IapKind.Noads).Select(kv => kv.Key).FirstOrDefault();
            PK.T(col, p, "Skip the ads for good?", 36);
            HomeMsg(col, p, "Every ad reward — the Lucky Wheel, Free Coins, the ×2 daily bonus —<br>pays straight away, with no video to watch.", 18);
            var row = PK.Row(col, p.P(20));
            PK.Btn(row, p, "blue", "Later", 220, 74, 28, Back);
            if (Purchases?.Unlocked.NoAds == true) PK.T(row, p, "Yours already!", 30);
            else if (noAdsId != null) PK.Btn(row, p, "green", ShopIapPrice(noAdsId), 220, 74, 28, () => { Back(); ShopIapBuy(noAdsId); });
        }

        private void HomeWait(Ctx c)
        {
            var p = c.P;
            var col = PK.Col(c.Root, p.P(16));
            col.style.position = Position.Absolute;
            col.style.left = new Length(50, LengthUnit.Percent);
            col.style.top = new Length(50, LengthUnit.Percent);
            col.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            col.style.alignItems = Align.Center;
            HomeSpinner(col, p, p.P(110), c);
            PK.T(col, p, "Please wait…", 28);
        }

        // ------------------------------------------------------------------ profile extras

        private void HomeAvatar(Ctx c)
        {
            var p = c.P;
            var pick = _homeAvaPick ?? HomeAvatarNow;
            var level = Economy?.IslandLevel ?? 1;
            var pop = PK.Popup(c.Root, p, 780, 0, "Choose avatar", () => { _homeAvaPick = null; Back(); });
            var grid = PK.Box(pop);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            PK.Pad(grid, p.P(10), p.P(26), p.P(10), p.P(10));
            for (var i = 0; i < HomeAvatars.Length; i++)
            {
                var a = HomeAvatars[i];
                var locked = i >= 8 && level < Wallet.Prices.Profile.AvatarUnlockLevel;
                var cell = PK.Box(grid);
                PK.Size(cell, p.P(110), p.P(110));
                cell.style.marginRight = i % 5 < 4 ? p.P(24) : 0;
                cell.style.marginBottom = i < 5 ? p.P(24) : 0;
                if (a == pick)
                {
                    var ring = PK.Inset(PK.Box(cell), -p.U(5), -p.U(5), -p.U(5), -p.U(5));
                    ring.style.backgroundColor = GameTheme.C("#ffd23a");
                    Kit.Round(ring, p.P(55) + p.U(5));
                }

                var ava = PK.Centre(PK.Inset(PK.Box(cell)));
                Kit.Round(ava, p.P(55));
                Kit.Picture(ava, GameArt.Radial("homeava", .5f, .5f, .5f, .5f, (0f, GameTheme.T("#5fc0ff")), (.999f, GameTheme.T("#1676d1")), (1f, GameTheme.T("#1676d1", 0f))));
                var frame = PK.Inset(PK.Box(cell), -p.P(110) * .07f, -p.P(110) * .07f, -p.P(110) * .07f, -p.P(110) * .07f);
                Kit.Picture(frame, GameArt.Get("ProfileFrame01_Border"));
                var ic = PK.Img(cell, a, p.P(110) * .62f);
                PK.Abs(ic, left: (p.P(110) - ic.style.width.value.value) / 2f, top: p.P(110) * .19f);
                if (locked) PK.Abs(PK.Img(cell, "Icon_ImageIcon_Lock01_s", p.P(44)), right: -p.P(6), bottom: -p.P(6));
                PK.Tap(cell, p, () =>
                {
                    if (locked) { Say("Unlocks at island level " + Wallet.Prices.Profile.AvatarUnlockLevel); return; }
                    _homeAvaPick = a;
                    MarkDirty();
                });
            }

            var row = PK.Row(pop);
            row.style.justifyContent = Justify.Center;
            row.style.marginTop = p.P(16);
            PK.Btn(row, p, "green", "Save", 220, 70, 28, () =>
            {
                PlayerPrefs.SetString("fv_avatar", pick);
                PlayerPrefs.Save();
                if (Profile is { Online: true } prof) _ = prof.SetAvatar(AvatarIdOf(pick));   // friends and the board see it
                _homeAvaPick = null;
                Back();
            });
        }

        private void HomeSaveFarm(Ctx c)
        {
            var p = c.P;
            var acc = Account;
            var pop = PK.Popup(c.Root, p, 600, 0, "Save your farm", Back);
            var col = PK.Col(pop, p.P(14));
            col.style.alignItems = Align.Center;
            PK.Pad(col, 0, p.P(20), 0, p.P(4));
            if (acc == null || !acc.SignedIn)
            {
                HomeMsg(col, p, "Connect to the internet to save your farm.", 18, "#bfe6ff");
                return;
            }
            if (acc.IsLinked)
            {
                var with = string.Join(" and ", acc.Linked.Select(l => l.ToString()));
                HomeMsg(col, p, $"Your farm is saved to your <b>{with}</b> account.<br>Sign in with it on a new phone to get it back.", 18, "#bfe6ff");
                PK.Btn(col, p, "green", "OK", 220, 70, 28, Back);
                return;
            }
            HomeMsg(col, p, "You're playing as a <b>guest</b>. Link an account to keep your farm<br>if you change phone — and to receive coupons.", 18, "#bfe6ff");
            var any = false;
            foreach (var (provider, colour) in new[] { (LinkProvider.Google, "blue"), (LinkProvider.Apple, "gray") })
            {
                if (!acc.CanLink(provider)) continue;
                any = true;
                var pv = provider;
                PK.Btn(col, p, colour, "  Continue with " + provider, 420, 74, 26, () => AccountLink(pv));
            }
            if (!any) HomeMsg(col, p, "Google and Apple sign-in work in the phone builds.", 16, "#9fd0ff");
            var reward = Wallet.Prices.LinkReward;
            if (reward.Gems > 0 || reward.Coins > 0)
            {
                var note = PK.Row(col, p.U(8));
                PK.Img(note, "Toggle01_Check_Green", p.P(24));
                var what = reward.Gems > 0 ? reward.Gems + "-gem" : PK.Num(reward.Coins) + "-coin";
                PK.N(note, p, $"Linked accounts get a {what} thank-you.", 15, GameTheme.C("#7dff6a"));
            }
        }

        private void HomeRate(Ctx c)
        {
            var p = c.P;
            HomeDim(c.Root, "#030c20", .8f);
            var col = PK.Col(c.Root, p.P(14));
            col.style.position = Position.Absolute;
            col.style.left = new Length(50, LengthUnit.Percent);
            col.style.top = p.P(p.Phone ? 60 : 240);
            col.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            col.style.alignItems = Align.Center;
            PK.T(col, p, "Enjoying Farmverse?", 30, GameTheme.T("#7fd0ff"));
            PK.T(col, p, "Leave 5 stars in the store!", 42);
            PK.Size(PK.Box(col), 1, p.P(30) - p.P(14));   // margin-top 30 on top of the gap
            var stars = PK.Row(col, p.P(8));
            foreach (var s in new[] { 90f, 120f, 160f, 120f, 90f }) PK.Img(stars, "Icon_ImageIcon_Star02_l", p.P(s));
            PK.Size(PK.Box(col), 1, p.P(40) - p.P(14));
            var row = PK.Row(col, p.P(26));
            PK.Btn(row, p, "blue", "Maybe later", 240, 74, 28, Back);
            PK.Btn(row, p, "yellow", "Rate", 240, 74, 30, () =>
            {
                PlayerPrefs.SetInt("fv_rated", 1);
                PlayerPrefs.Save();
                GameLinks.Rate();
                Back();
            });
        }
    }
}
