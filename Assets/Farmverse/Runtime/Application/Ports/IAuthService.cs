#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Application.Ports
{
    /// <summary>The signed-in Firebase user, as the game needs it (never an email).</summary>
    public sealed class AuthUser
    {
        public AuthUser(string uid, bool isAnonymous)
        {
            Uid = uid;
            IsAnonymous = isAnonymous;
        }

        public string Uid { get; }

        /// <summary>Guests may play but not list on the marketplace (decision D2).</summary>
        public bool IsAnonymous { get; }
    }

    /// <summary>Firebase Auth behind a port: anonymous sign-in (wallet sign-in comes later, 13 §5).</summary>
    public interface IAuthService
    {
        /// <summary>The current user; null when signed out. Changes on sign-in and sign-out.</summary>
        IReadOnlyReactiveProperty<AuthUser?> User { get; }

        Task<Result<AuthUser>> SignInAnonymously(CancellationToken cancellation);

        Task SignOut();
    }
}
