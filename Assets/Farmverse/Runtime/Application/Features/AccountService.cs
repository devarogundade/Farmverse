#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The player's account: a guest until linked to Google or Apple (Save Farm). Linking keeps the uid and
    /// the farm, and pays the catalogue's thank-you once. When the Google or Apple account already belongs to
    /// another farm (a second phone), the player can switch to it: the game restarts on that account and the
    /// island comes back from its cloud save. Deleting the account removes everything on the server, then
    /// starts the game over as a new guest.
    /// </summary>
    public sealed class AccountService
    {
        private readonly IAuthService _auth;
        private readonly ServerCalls _server;
        private readonly Action _restart;
        private readonly Action _forgetDevice;

        public AccountService(IAuthService auth, ServerCalls server, Action restart, Action forgetDevice)
        {
            _auth = auth;
            _server = server;
            _restart = restart;
            _forgetDevice = forgetDevice;
        }

        /// <summary>Signed in (as a guest or linked).</summary>
        public bool SignedIn => _auth.User.Value != null;

        /// <summary>Linked to Google or Apple: the farm is safe if the phone is lost.</summary>
        public bool IsLinked => _auth.Linked.Count > 0;

        public IReadOnlyList<LinkProvider> Linked => _auth.Linked;

        /// <summary>This phone can link (or sign in with) the provider.</summary>
        public bool CanLink(LinkProvider provider) => _auth.CanLink(provider);

        /// <summary>
        /// Link this farm to the provider, then collect the thank-you. Ok carries the reward paid (null when it
        /// was collected before). <see cref="ErrorCode.AccountInUse"/>: that account already has a farm.
        /// </summary>
        public async Task<Result<PlayerClaimLinkRewardResponse?>> Link(LinkProvider provider, CancellationToken cancellation = default)
        {
            var linked = await _auth.Link(provider, cancellation);
            if (linked.IsFailure) return Result<PlayerClaimLinkRewardResponse?>.Fail(linked.Error);
            var reward = await _server.ClaimLinkReward(cancellation);
            return Result<PlayerClaimLinkRewardResponse?>.Ok(reward.IsOk ? reward.Value : null);
        }

        /// <summary>Switch to the farm already linked to the provider; the game restarts on success.</summary>
        public async Task<Result> SwitchTo(LinkProvider provider, CancellationToken cancellation = default)
        {
            var signedIn = await _auth.SignIn(provider, cancellation);
            if (signedIn.IsFailure) return Result.Fail(signedIn.Error);
            _restart();
            return Result.Ok();
        }

        /// <summary>Delete the account on the server, sign out, forget this phone's farm and start over.</summary>
        public async Task<Result> Delete(CancellationToken cancellation = default)
        {
            var deleted = await _server.AccountDelete(cancellation);
            if (deleted.IsFailure) return Result.Fail(deleted.Error);
            await _auth.SignOut();
            _forgetDevice();
            _restart();
            return Result.Ok();
        }
    }
}
