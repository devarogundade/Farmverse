#nullable enable
using Farmverse.Application.Features;

namespace Farmverse.Presentation.UI.Game
{
    // The account: Delete Account (wallet sign-in comes later). Set by GameRoot; null in the Island scene
    // opened on its own.
    public sealed partial class GameUi
    {
        public static AccountService? Account { get; set; }

        private bool _accountBusy;

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
                Text = "Your farm, gems, friends and progress will be <b>deleted for good</b>.",
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
