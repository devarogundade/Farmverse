#nullable enable
using System;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using Farmverse.Core.Results;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Friends (R44): your invite code to share, a box for someone else's code, and your friends list,
    // all live from the server (friends-createInvite, friends-accept, players/{uid}/friends).
    public sealed partial class GameUi
    {
        private static readonly bool RegFriends = Register("friends", UiLayer.Popup, (ui, c) => ui.FriendsPopup(c), .55f);

        /// <summary>The server side of friends, handed over by the composition root; null when the island runs on its own (editor).</summary>
        public static FriendsService? Friends { get; set; }

        private bool _frBusy;
        private string _frCode = "";
        private string _frNote = "";
        private bool _frNoteBad;

        private void FriendsPopup(Ctx c)
        {
            var p = c.P;
            var f = Friends;
            c.Watch(() => (f?.Version.Value ?? -1, _frBusy, _frNote));
            var pop = PK.Popup(c.Root, p, 900, 860, "Friends", Back);
            var body = PK.Col(pop, p.P(18));
            body.style.flexGrow = 1;
            PK.Pad(body, p.P(6), p.P(20), p.P(6), 0);

            if (f == null || !f.Ready.Value)
            {
                var wait = PK.Centre(PK.Col(body, p.P(20)));
                wait.style.flexGrow = 1;
                PK.Img(wait, "Icon_Friends", p.P(120));
                HomeMsg(wait, p, f == null
                    ? "Friends need the full game.<br>Start from the <b>Boot</b> scene to sign in."
                    : "Connecting to the farm network…<br>Friends appear once you're online.", 24);
                return;
            }

            // a friend added from an invite link while the app opened
            if (f.JustAdded is { } added)
            {
                f.JustAdded = null;
                _frNote = $"You and {added.DisplayName} are now friends!";
                _frNoteBad = false;
            }

            ReferralCard(body, p);
            FriendsInvite(body, p, f);
            FriendsEnterCode(body, p, f);
            if (_frNote.Length > 0)
                PK.N(body, p, _frNote, 20, _frNoteBad ? GameTheme.C("#ff7e93") : GameTheme.C("#7dff6a"), TextAnchor.MiddleCenter, wrap: true);
            FriendsList(body, p, f);
        }

        /// <summary>Your code: make one, then copy the link to send it.</summary>
        private void FriendsInvite(VisualElement body, Pt p, FriendsService f)
        {
            var card = FriendsCard(body, p);
            var text = PK.Col(card, p.P(4));
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            if (f.Invite is { } invite)
            {
                PK.N(text, p, "Your invite code", 18, GameTheme.T("#9fd0ff"));
                PK.T(text, p, invite.Code.Substring(0, 4) + " " + invite.Code.Substring(4), 40, GameTheme.C("#ffd23a"));
                var days = Math.Max(1, (int)Math.Ceiling((invite.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 86_400_000.0));
                PK.N(text, p, $"{invite.UsesLeft} uses left · expires in {days} day{(days == 1 ? "" : "s")}", 16, GameTheme.T("#cfe8ff"));
                PK.Btn(card, p, "green", "Copy link", 200, 66, 24, () =>
                {
                    GameLinks.Copy(invite.Link);
                    Say("Invite link copied — send it to a friend");
                });
            }
            else
            {
                PK.N(text, p, "Invite friends", 22, Color.white);
                PK.N(text, p, "Make a code and send it to a friend.", 17, GameTheme.T("#cfe8ff"), wrap: true);
                PK.Btn(card, p, "green", "Get my code", 220, 66, 24, _frBusy, () =>
                {
                    if (_frBusy) return;
                    _frBusy = true;
                    _frNote = "";
                    _ = FriendsRun(f.CreateInvite(), _ => "");
                });
            }
        }

        /// <summary>A friend's code, typed or pasted (the whole link works too).</summary>
        private void FriendsEnterCode(VisualElement body, Pt p, FriendsService f)
        {
            var card = FriendsCard(body, p);
            var box = PK.Row(card);
            box.style.flexGrow = 1;
            box.style.height = p.P(66);
            box.style.backgroundColor = Color.white;
            Kit.Round(box, p.P(14));
            Kit.Border(box, p.U(3), Color.black);
            box.style.overflow = Overflow.Hidden;

            var field = new TextField { value = _frCode, maxLength = 64 };
            field.style.flexGrow = 1;
            field.style.marginLeft = field.style.marginRight = field.style.marginTop = field.style.marginBottom = 0;
            var font = GameArt.Display;
            var input = field.Q(className: TextField.inputUssClassName) ?? field;
            foreach (var e in new[] { field, input, input.Q<TextElement>() })
            {
                if (e == null) continue;
                if (font != null) e.style.unityFontDefinition = FontDefinition.FromFont(font);
                e.style.fontSize = p.P(28);
                e.style.color = Color.black;
                e.style.unityTextAlign = TextAnchor.MiddleCenter;
                e.style.backgroundColor = Color.clear;
                Kit.Border(e, 0, Color.clear);
            }
            input.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => _frCode = e.newValue ?? "");
            box.Add(field);
            if (_frCode.Length == 0)
            {
                var hint = PK.N(box, p, "Friend's code", 24, GameTheme.T("#7a8aa0"), TextAnchor.MiddleCenter);
                PK.Inset(hint);
                hint.pickingMode = PickingMode.Ignore;
                field.RegisterCallback<FocusInEvent>(_ => hint.style.display = DisplayStyle.None);
            }

            var add = PK.Btn(card, p, "blue", "Add", 150, 66, 26, _frBusy, () =>
            {
                if (_frBusy) return;
                if (FriendsService.CodeFrom(_frCode) == null)
                {
                    _frNote = "A code is 8 letters and numbers, like ABCD 2345.";
                    _frNoteBad = true;
                    MarkDirty();
                    return;
                }
                _frBusy = true;
                _frNote = "";
                _ = FriendsRun(f.Accept(_frCode), r =>
                {
                    _frCode = "";
                    return $"You and {r.DisplayName} are now friends!";
                });
            });
            add.style.marginLeft = p.P(14);
        }

        /// <summary>Everyone you're friends with, newest first.</summary>
        private void FriendsList(VisualElement body, Pt p, FriendsService f)
        {
            var head = PK.Row(body);
            PK.T(head, p, "Your friends", 24);
            PK.N(head, p, f.Friends.Count.ToString(), 20, GameTheme.T("#9fd0ff")).style.marginLeft = p.P(10);

            var sv = ShopScroller(body, false, p.P(12));
            sv.style.minHeight = p.P(120);
            var list = sv.contentContainer;
            if (f.Friends.Count == 0)
            {
                PK.N(list, p, "No friends yet. Share your code, or add a friend's.", 20, GameTheme.T("#cfe8ff"), TextAnchor.MiddleCenter, wrap: true)
                    .style.marginTop = p.P(24);
                return;
            }

            foreach (var friend in f.Friends)
            {
                var row = PK.Row(list, p.P(16));
                row.style.flexShrink = 0;
                row.style.marginBottom = p.P(10);
                PK.Pad(row, p.P(12), p.P(10), p.P(16), p.P(10));
                row.style.backgroundColor = GameTheme.T("#041634", .45f);
                Kit.Round(row, p.P(16));
                PlayAvatar(row, p, FriendsAvatar(friend), 72);
                var col = PK.Col(row, p.P(2));
                col.style.flexGrow = 1;
                PK.T(col, p, friend.Name, 26);
                var since = DateTimeOffset.FromUnixTimeMilliseconds(friend.Since).ToLocalTime().ToString("d MMM yyyy");
                PK.N(col, p, $"ID {friend.ShortId} · friends since {since}", 16, GameTheme.T("#9fd0ff"));
            }
        }

        private static VisualElement FriendsCard(VisualElement body, Pt p)
        {
            var card = PK.Row(body, p.P(16));
            card.style.flexShrink = 0;
            PK.Pad(card, p.P(20), p.P(14), p.P(16), p.P(14));
            card.style.backgroundColor = GameTheme.T("#041634", .45f);
            Kit.Round(card, p.P(18));
            return card;
        }

        /// <summary>A picture for a friend: their chosen face isn't stored yet, so one of the game's avatars, the same every time.</summary>
        private static string FriendsAvatar(FriendRow friend)
        {
            var h = 0;
            foreach (var ch in friend.ShortId) h = h * 31 + ch;
            return HomeAvatars[(h & 0x7fffffff) % HomeAvatars.Length];
        }

        /// <summary>Run a friends call: busy while it runs, then a note (the success line, or what went wrong).</summary>
        private async System.Threading.Tasks.Task FriendsRun<T>(System.Threading.Tasks.Task<Result<T>> call, Func<T, string> success)
        {
            var result = await call;
            _frBusy = false;
            if (result.IsOk)
            {
                _frNote = success(result.Value);
                _frNoteBad = false;
                if (_frNote.Length > 0) Farmverse.Presentation.World.Island.IslandAudio.Play("cheer", null, 0.7f, 0f, 1f);
            }
            else
            {
                _frNote = result.Error.Code switch
                {
                    ErrorCode.InviteInvalid => "That code doesn't work — it may have expired or been used up.",
                    ErrorCode.AlreadyFriends => "You're already friends.",
                    ErrorCode.NetworkRequired or ErrorCode.Timeout => "No connection. Try again in a moment.",
                    _ => "Something went wrong. Try again.",
                };
                _frNoteBad = true;
            }
            MarkDirty();
        }
    }
}
