#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The referral program: invite someone; when their island reaches the catalogue's level you both get its
    // reward. The server keeps the referral and gives the rewards; the game claims the referral when the
    // invited player levels up, and collects each reward once (coins onto the island, gems into the wallet).
    public sealed partial class GameUi
    {
        /// <summary>The catalogue's referral: the level, and what each player gets.</summary>
        public static int ReferralLevel => Wallet.Prices.Referral.Level;
        public static long ReferralCoins => Wallet.Prices.Referral.Reward.Coins;
        public static long ReferralGems => Wallet.Prices.Referral.Reward.Gems;

        private const string ReferralDoneKey = "fv_referral_done";

        private static readonly bool RegRewardClaim = Register("rewardclaim", UiLayer.Popup, (ui, c) => ui.RewardClaimPopup(c), .55f);

        /// <summary>Rewards from the server, handed over by the composition root; null when the island runs on its own.</summary>
        public static RewardsService? Rewards { get; set; }

        private bool _refBusy;
        private float _refNextTry;
        private bool _rewardBusy;

        /// <summary>Every frame: claim the referral once the island reaches the level, and offer waiting rewards.</summary>
        private void ReferralTick()
        {
            var f = Friends;
            var host = Host;
            if (f != null && f.Ready.Value && host != null && !_refBusy && Time.unscaledTime >= _refNextTry
                && host.Economy.BestLevel >= ReferralLevel && PlayerPrefs.GetInt(ReferralDoneKey, 0) == 0)
            {
                _refBusy = true;
                _ = ReferralRun(f, JsonUtility.ToJson(host.CurrentState()));
            }

            // a reward waiting: show it when nothing else is up, on the island or home
            var rewards = Rewards;
            // (referral rewards pop up; mail and weekly prizes wait in the inbox)
            var referral = rewards?.Pending.FirstOrDefault(r => r.Reason == RewardReason.ReferralInviter || r.Reason == RewardReason.ReferralInvitee);
            if (referral != null && !_rewardBusy && _popups.Count == 0 && _pages.Count == 0
                && (_baseId == "play" || _baseId == "home"))
                Open("rewardclaim", referral);
        }

        private async Task ReferralRun(FriendsService f, string islandJson)
        {
            var result = await f.ClaimReferral(islandJson);
            _refBusy = false;
            if (result.IsFailure)
            {
                _refNextTry = Time.unscaledTime + 120f;   // offline or busy: try again in a while
                return;
            }
            if (result.Value.Reason == ReferralOutcome.Level)
            {
                _refNextTry = Time.unscaledTime + 600f;   // the server's copy is behind: mirror again later
                return;
            }
            // rewarded now, rewarded before, or never invited: nothing more to do on this device
            PlayerPrefs.SetInt(ReferralDoneKey, 1);
            PlayerPrefs.Save();
        }

        private void RewardClaimPopup(Ctx c)
        {
            var p = c.P;
            var reward = c.Args as PendingReward;
            var rewards = Rewards;
            if (reward == null || rewards == null)
            {
                HomeSimple(c, "Reward", "Nothing to collect.", new[] { ("green", "OK", (System.Action)Back) });
                return;
            }

            var pop = PK.Popup(c.Root, p, 680, 0, "Referral reward!", null);
            var body = PK.Col(pop, p.P(20));
            body.style.alignItems = Align.Center;
            PK.Pad(body, p.P(10), p.P(26), p.P(10), p.P(6));
            HomeMsg(body, p, reward.Reason == RewardReason.ReferralInviter
                ? $"<b>{reward.About}</b> reached Level {ReferralLevel} on your invite!<br>Here's your thank-you."
                : $"You reached Level {ReferralLevel}!<br>Thanks to <b>{reward.About}</b>'s invite, you both get a gift.", 22);
            var row = PK.Row(body, p.P(24));
            if (reward.Coins > 0) PK.Slot(row, p, "ResourceBar_Icon_Gold", PK.Num(reward.Coins), "Yellow", 130);
            if (reward.Gems > 0) PK.Slot(row, p, "ResourceBar_Icon_Gem", reward.Gems.ToString(), "Purple", 130);
            PK.Btn(body, p, "green", "Collect", 260, 74, 30, _rewardBusy, () =>
            {
                if (_rewardBusy) return;
                _rewardBusy = true;
                MarkDirty();
                _ = RewardCollect(rewards, reward);
            });
        }

        private async Task RewardCollect(RewardsService rewards, PendingReward reward)
        {
            var result = await rewards.Claim(reward.Id);
            _rewardBusy = false;
            if (result.IsFailure)
            {
                Say("Couldn't collect it — check your connection and try again");
                MarkDirty();
                return;
            }
            Back();
            if (!result.Value.Fresh) return;   // collected already (another device)
            var coins = (int)result.Value.Coins;
            var gems = (int)result.Value.Gems;
            if (coins > 0) Economy?.AddCoins(coins);
            // the gems went straight into the server wallet (rewards-claim); the balance updates by itself
            IslandAudio.Play("coin");
            Say($"+{PK.Num(coins)} coins  +{gems} gems");
        }

        /// <summary>The program, explained on the Friends screen.</summary>
        private static void ReferralCard(VisualElement body, Pt p)
        {
            var card = FriendsCard(body, p);
            card.style.backgroundColor = GameTheme.T("#3a2a06", .55f);
            PK.Img(card, "Icon_Gift", p.P(64));
            var text = PK.Col(card, p.P(2));
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            PK.T(text, p, "Invite friends, earn rewards", 22, GameTheme.C("#ffd23a"));
            PK.N(text, p, $"When a friend you invite reaches Level {ReferralLevel}, you both get {ReferralCoins} coins and {ReferralGems} gems.", 17, GameTheme.T("#ffeccc"), wrap: true);
            var prize = PK.Row(card, p.P(6));
            prize.style.flexShrink = 0;          // the text wraps instead; the prize never squeezes into the edge
            prize.style.marginLeft = p.P(12);
            prize.style.marginRight = p.P(8);    // with the card's own padding: as much room as on the left
            PK.Img(prize, "ResourceBar_Icon_Gold", p.P(44));
            PK.T(prize, p, ReferralCoins.ToString(), 22);
            PK.Img(prize, "ResourceBar_Icon_Gem", p.P(44)).style.marginLeft = p.P(8);
            PK.T(prize, p, ReferralGems.ToString(), 22);
        }
    }
}
