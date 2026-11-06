#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The player's account: a guest (anonymous sign-in) until wallet sign-in arrives. Deleting the account
    /// removes everything on the server, then starts the game over as a new guest.
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

        /// <summary>Signed in (as a guest).</summary>
        public bool SignedIn => _auth.User.Value != null;

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
