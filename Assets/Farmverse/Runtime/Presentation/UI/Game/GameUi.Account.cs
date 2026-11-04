#nullable enable
using System;
using Farmverse.Application.Features;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Presentation.World.Island;

namespace Farmverse.Presentation.UI.Game
{
    // The account: Save Farm (link Google or Apple, or switch to the farm already on that account) and
    // Delete Account. Set by GameRoot; null in the Island scene opened on its own.
    public sealed partial class GameUi
    {
        public static AccountService? Account { get; set; }

        private bool _accountBusy;

        /// <summary>Link this farm to Google or Apple; when that account already has a farm, offer to switch to it.</summary>
        private async void AccountLink(LinkProvider provider)
        {
            var acc = Account;
            if (acc == null || _accountBusy) return;
            _accountBusy = true;
            Open("wait");
            var result = await acc.Link(provider);
            _accountBusy = false;
            if (TopPopup == "wait") Back();
            if (result.IsOk)
            {
                var reward = result.Value;
                if (reward != null && reward.Fresh && reward.Coins > 0) Economy?.AddCoins((int)reward.Coins);
                IslandAudio.Play("cheer");
                Back();   // Save Farm
                Say(reward != null && reward.Gems > 0 ? $"Farm saved! +{reward.Gems} gems" : "Farm saved!");
                MarkDirty();
                return;
            }
            switch (result.Error.Code)
            {
                case ErrorCode.Cancelled:
                    return;
                case ErrorCode.AccountInUse:
                    Open("confirm", new HomeConfirmArgs
                    {
                        Title = "Farm found",
                        Text = $"This {provider} account already has a farm.<br>Switch to it? <b>The farm on this phone will be replaced.</b>",
                        Ok = "Switch",
                        OnOk = () => AccountSwitch(provider),
                    });
                    return;
                default:
                    Say("Couldn't link the account — check your connection and try again");
                    return;
            }
        }

        private async void AccountSwitch(LinkProvider provider)
        {
            var acc = Account;
            if (acc == null || _accountBusy) return;
            _accountBusy = true;
            Open("wait");
            var result = await acc.SwitchTo(provider);   // restarts the game on success
            _accountBusy = false;
            if (result.IsOk) return;
            if (TopPopup == "wait") Back();
            if (result.Error.Code != ErrorCode.Cancelled) Say("Couldn't sign in — try again");
        }

        /// <summary>Settings ▸ Delete Account: asks twice, then deletes everything and starts over as a new guest.</summary>
        private void AccountDeleteAsk()
        {
            if (Account is not { SignedIn: true })
            {
                Say("Connect to the internet to delete your account");
                return;
            }
            Open("confirm", new HomeConfirmArgs
            {
                Title = "Delete account?",
                Text = "Your farm, gems, friends and progress will be <b>deleted for good</b>.<br>Purchases can't be refunded.",
                Ok = "Delete",
                OnOk = () => Open("confirm", new HomeConfirmArgs
                {
                    Title = "Are you sure?",
                    Text = "This can't be undone.",
                    Ok = "Delete forever",
                    OnOk = AccountDelete,
                }),
            });
        }

        private async void AccountDelete()
        {
            var acc = Account;
            if (acc == null || _accountBusy) return;
            _accountBusy = true;
            Open("wait");
            var result = await acc.Delete();   // restarts the game on success
            _accountBusy = false;
            if (result.IsOk) return;
            if (TopPopup == "wait") Back();
            Say("Couldn't delete the account — check your connection and try again");
        }
    }
}
