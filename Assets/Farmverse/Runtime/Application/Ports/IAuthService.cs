#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Application.Ports
{
    /// <summary>The signed-in player, as the game needs it.</summary>
    public sealed class AuthUser
    {
        public AuthUser(string uid, bool isAnonymous, string accountId = "")
        {
            Uid = uid;
            IsAnonymous = isAnonymous;
            AccountId = accountId;
        }

        /// <summary>The game uid (opaque; documents and calls use it).</summary>
        public string Uid { get; }

        /// <summary>No wallet behind the session (tests and tools only; players always sign in with a wallet).</summary>
        public bool IsAnonymous { get; }

        /// <summary>The NEAR account the player signed in with (`alice.near`); empty when anonymous.</summary>
        public string AccountId { get; }
    }

    /// <summary>
    /// Sign-in behind a port (docs 13 §5.2): a NEAR wallet on the web page (NEP-413), or the dev
    /// account in the editor.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>The current user; null when signed out. Changes on sign-in and sign-out.</summary>
        IReadOnlyReactiveProperty<AuthUser?> User { get; }

        /// <summary>Signs in (on the web: the page asks the player to connect their wallet if needed).</summary>
        Task<Result<AuthUser>> SignIn(CancellationToken cancellation);

        Task SignOut();
    }
}
