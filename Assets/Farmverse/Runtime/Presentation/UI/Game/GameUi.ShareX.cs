#nullable enable
using System;
using System.Threading.Tasks;
using Farmverse.Contracts;
using Farmverse.Core.Results;
using Farmverse.Presentation.World.Island;
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    // The daily X share (Shop ▸ Offers): opens X with one of the catalogue's preset posts (picked at random,
    // the player's invite link in it), then opens today's share chest on the server: its coins go to the
    // island, its gems to the wallet, once per UTC day. Sharing again the same day is welcome but pays nothing.
    public sealed partial class GameUi
    {
        private const string ShareFallbackLink = "https://play.farmverse.world";

        private bool _shareBusy;

        /// <summary>Today's share reward is already collected (this UTC day, as the server's shop doc says).</summary>
        private static bool ShareClaimedToday => Wallet.Shop?.ChestClaimed(ChestId.Share, ShopUtcDay) ?? false;

        /// <summary>The Offers card: what it pays, FREE until collected, then the time to the next one.</summary>
        private ShopItem ShopShareItem()
        {
            var claimed = ShareClaimedToday;
            var chips = new System.Collections.Generic.List<(string, string)>();
            if (Wallet.Prices.Chests.TryGetValue(ChestIdWire.ToWire(ChestId.Share), out var b))
            {
                if (b.Coins > 0) chips.Add(("ResourceBar_Icon_Gold", "+" + PK.Num(b.Coins)));
                if (b.Gems > 0) chips.Add(("ResourceBar_Icon_Gem", "+" + PK.Num(b.Gems)));
            }
            return new ShopItem
            {
                kind = "hero", frame = "g5", crown = true,
                head = claimed ? "<size=76%>Next in:</size> <color=#ff5a6e>" + ShopUntilMidnight() + "</color>" : "<size=76%>Every day</size>",
                name = "DAILY X SHARE", art = "Icon_MenuIcon04_News", glow = !claimed,
                sub = claimed ? "Shared today. Post again anytime!" : "Post about Farmverse on X",
                chips = chips.Count > 0 ? chips.ToArray() : null,
                free = !claimed, timer = claimed ? "Tomorrow" : null,
                tap = ShopShareX,
            };
        }

        /// <summary>Opens X with a preset post and the invite link, then collects today's share reward.</summary>
        private async void ShopShareX()
        {
            if (_shareBusy) return;
            var shop = Wallet.Shop;
            if (shop == null || !shop.Online)
            {
                IslandAudio.Play("error");
                Say("Connect to the internet to share");
                return;
            }

            _shareBusy = true;
            try
            {
                var text = SharePost().Replace("{link}", await ShareInviteLink());
                GameLinks.Open("https://x.com/intent/post?text=" + Uri.EscapeDataString(text), "Post on X");

                if (ShareClaimedToday)
                {
                    Say("Thanks for sharing! Today's reward is already collected");
                    return;
                }
                var host = Host;
                if (host == null) return;
                var result = await shop.ClaimChest(ChestId.Share, JsonUtility.ToJson(host.CurrentState()));
                if (result.IsFailure)
                {
                    Say(result.Error.Code == ErrorCode.ChestClaimed
                        ? "Thanks for sharing! Today's reward is already collected"
                        : "Couldn't collect the reward. Check your connection and try again");
                    return;
                }
                if (!result.Value.Fresh) return;   // collected already (another device)
                if (result.Value.Coins > 0) Economy?.AddCoins((int)result.Value.Coins);
                // the gems went straight into the server wallet; the balance updates by itself
                IslandAudio.Play("coin");
                Say($"Thanks for sharing!  +{PK.Num(result.Value.Coins)} coins  +{result.Value.Gems} gems");
            }
            finally
            {
                _shareBusy = false;
                MarkDirty();
            }
        }

        /// <summary>One of the catalogue's preset posts, at random (a plain one if the catalogue has none).</summary>
        private static string SharePost()
        {
            var posts = Wallet.Prices.SharePosts;
            if (posts == null || posts.Count == 0) return "Having fun on my Farmverse farm, built on @NEARProtocol. Come play: {link} #NEARHackathon";
            return posts[UnityEngine.Random.Range(0, posts.Count)];
        }

        /// <summary>
        /// The player's invite link: this session's invite while it has uses and over a day left, else a fresh
        /// one; the game's own address when there's no invite to give (not signed in, or the call failed).
        /// </summary>
        private static async Task<string> ShareInviteLink()
        {
            var f = Friends;
            if (f == null) return ShareFallbackLink;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (f.Invite is { } have && have.UsesLeft > 0 && have.ExpiresAt - now > 86_400_000L) return have.Link;
            if (!f.Ready.Value) return ShareFallbackLink;
            var made = await f.CreateInvite();
            return made.IsFailure ? ShareFallbackLink : made.Value.Link;
        }
    }
}
