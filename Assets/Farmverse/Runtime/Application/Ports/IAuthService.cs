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

    public enum LinkProvider
    {
        Google = 1,
        Apple = 2,
    }

    /// <summary>Firebase Auth behind a port: anonymous first, link a provider later (02 §5).</summary>
    public interface IAuthService
    {
        /// <summary>The current user; null when signed out. Changes on sign-in, link and sign-out.</summary>
        IReadOnlyReactiveProperty<AuthUser?> User { get; }

        Task<Result<AuthUser>> SignInAnonymously(CancellationToken cancellation);

        /// <summary>Link the anonymous account to a provider; the uid is kept.</summary>
        Task<Result<AuthUser>> Link(LinkProvider provider, CancellationToken cancellation);

        /// <summary>
        /// Sign in to the account already linked to <paramref name="provider"/> (another phone's farm). The uid
        /// changes: the game restarts on the new account.
        /// </summary>
        Task<Result<AuthUser>> SignIn(LinkProvider provider, CancellationToken cancellation);

        /// <summary>The providers the current account is linked to (empty for a guest).</summary>
        System.Collections.Generic.IReadOnlyList<LinkProvider> Linked { get; }

        /// <summary>False when this platform has no way to link <paramref name="provider"/> (the editor, desktop).</summary>
        bool CanLink(LinkProvider provider);

        Task SignOut();
    }
}
