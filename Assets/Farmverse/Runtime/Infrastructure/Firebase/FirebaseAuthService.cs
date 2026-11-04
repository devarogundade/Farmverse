#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;
using Firebase;
using Firebase.Auth;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// One way to link the signed-in user to a provider. Which flow a provider
    /// uses depends on the platform (see <see cref="LinkFlows"/>); every flow ends
    /// in Firebase Auth, so the uid — and the farm — never changes.
    /// </summary>
    public interface ILinkFlow
    {
        LinkProvider Provider { get; }
        Task<AuthResult> Link(FirebaseUser user, CancellationToken cancellation);

        /// <summary>Sign in with the provider (switching to the account that owns it).</summary>
        Task<AuthResult> SignIn(FirebaseAuth auth, CancellationToken cancellation);
    }

    /// <summary>Firebase's own OAuth flow (browser tab or custom tab): Apple on Android, no native SDK.</summary>
    public sealed class FederatedLinkFlow : ILinkFlow
    {
        private readonly string _providerId;

        public FederatedLinkFlow(LinkProvider provider, string providerId)
        {
            Provider = provider;
            _providerId = providerId;
        }

        public LinkProvider Provider { get; }

        public Task<AuthResult> Link(FirebaseUser user, CancellationToken cancellation)
        {
            // no scopes: the game never asks for a name or an email
            var data = new FederatedOAuthProviderData { ProviderId = _providerId };
            return user.LinkWithProviderAsync(new FederatedOAuthProvider(data));
        }

        public Task<AuthResult> SignIn(FirebaseAuth auth, CancellationToken cancellation) =>
            auth.SignInWithProviderAsync(new FederatedOAuthProvider(new FederatedOAuthProviderData { ProviderId = _providerId }));
    }

    /// <summary>A native sign-in produces tokens, Firebase turns them into a credential (Google; Apple on iOS).</summary>
    public sealed class CredentialLinkFlow : ILinkFlow
    {
        private readonly Func<CancellationToken, Task<Credential>> _credential;

        public CredentialLinkFlow(LinkProvider provider, Func<CancellationToken, Task<Credential>> credential)
        {
            Provider = provider;
            _credential = credential;
        }

        public LinkProvider Provider { get; }

        public async Task<AuthResult> Link(FirebaseUser user, CancellationToken cancellation)
        {
            var credential = await _credential(cancellation);
            return await user.LinkWithCredentialAsync(credential);
        }

        public async Task<AuthResult> SignIn(FirebaseAuth auth, CancellationToken cancellation)
        {
            var credential = await _credential(cancellation);
            return await auth.SignInAndRetrieveDataWithCredentialAsync(credential);
        }
    }

    /// <summary>A native sign-in step failed in a way the player should hear about (or chose to cancel).</summary>
    public sealed class LinkFlowException : Exception
    {
        public LinkFlowException(ErrorCode code, string message) : base(message) => Code = code;

        public ErrorCode Code { get; }
    }

    /// <summary>
    /// The local emulators only: one email account per developer machine, so the same farm
    /// comes back every Play (an anonymous sign-in cannot be refreshed through the emulator
    /// by the desktop SDK). The password is generated once and kept in UserSettings, which
    /// is not committed; the account exists only in the local Auth emulator.
    /// </summary>
    public sealed class LocalDevAccount
    {
        private const string File = "UserSettings/farmverse-local-account.txt";

        private LocalDevAccount(string email, string password)
        {
            Email = email;
            Password = password;
        }

        public string Email { get; }
        public string Password { get; }

        public static LocalDevAccount Load()
        {
            if (System.IO.File.Exists(File))
            {
                var lines = System.IO.File.ReadAllLines(File);
                if (lines.Length >= 2 && lines[0].Contains("@")) return new LocalDevAccount(lines[0].Trim(), lines[1].Trim());
            }
            var account = new LocalDevAccount("farmer-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "@farmverse.test", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory("UserSettings");
            System.IO.File.WriteAllLines(File, new[] { account.Email, account.Password });
            return account;
        }
    }

    /// <summary>Anonymous first, link later (02 §5). The uid never changes on link, so progress is kept.</summary>
    public sealed class FirebaseAuthService : IAuthService, IDisposable
    {
        private readonly FirebaseAuth _auth;
        private readonly IMainThread _main;
        private readonly ILog _log;
        private readonly ILinkFlow[] _flows;
        private readonly ReactiveProperty<AuthUser?> _user = new ReactiveProperty<AuthUser?>(null);
        private readonly LocalDevAccount? _local;

        public FirebaseAuthService(FirebaseAuth auth, IMainThread main, ILog log, params ILinkFlow[] flows)
            : this(auth, main, log, null, flows)
        {
        }

        public FirebaseAuthService(FirebaseAuth auth, IMainThread main, ILog log, LocalDevAccount? local, params ILinkFlow[] flows)
        {
            _local = local;
            _auth = auth;
            _main = main;
            _log = log.For("Auth");
            _flows = flows;
            _auth.StateChanged += OnStateChanged;
            _user.Value = ToUser(_auth.CurrentUser);
        }

        public IReadOnlyReactiveProperty<AuthUser?> User => _user;

        public async Task<Result<AuthUser>> SignInAnonymously(CancellationToken cancellation)
        {
            try
            {
                var result = _local != null ? await LocalSignIn(_local) : await _auth.SignInAnonymouslyAsync();
                var user = ToUser(result.User)!;
                _user.Value = user;
                return Result<AuthUser>.Ok(user);
            }
            catch (Exception error)
            {
                _log.Warn("anonymous sign-in failed", null, error);
                return Result<AuthUser>.Fail(AppError.FromException(ErrorCode.NetworkRequired, error));
            }
        }

        private async Task<AuthResult> LocalSignIn(LocalDevAccount account)
        {
            try
            {
                return await _auth.SignInWithEmailAndPasswordAsync(account.Email, account.Password);
            }
            catch (Exception)
            {
                // first Play on this machine, or the emulator was started fresh
                return await _auth.CreateUserWithEmailAndPasswordAsync(account.Email, account.Password);
            }
        }

        public async Task<Result<AuthUser>> Link(LinkProvider provider, CancellationToken cancellation)
        {
            var current = _auth.CurrentUser;
            if (current == null) return Result<AuthUser>.Fail(AppError.Of(ErrorCode.Unauthenticated));
            ILinkFlow? flow = null;
            foreach (var candidate in _flows) if (candidate.Provider == provider) flow = candidate;
            if (flow == null) return Result<AuthUser>.Fail(AppError.Of(ErrorCode.Unsupported, ("provider", provider.ToString())));
            try
            {
                var result = await flow.Link(current, cancellation);
                var user = ToUser(result.User)!;
                _user.Value = user;
                return Result<AuthUser>.Ok(user);
            }
            catch (OperationCanceledException)
            {
                return Result<AuthUser>.Fail(AppError.Of(ErrorCode.Cancelled));
            }
            catch (LinkFlowException error)
            {
                if (error.Code != ErrorCode.Cancelled) _log.Warn("native sign-in failed", null, error);
                return Result<AuthUser>.Fail(AppError.FromException(error.Code, error));
            }
            catch (Exception error)
            {
                var code = Classify(error);
                if (code != ErrorCode.Cancelled) _log.Warn("link failed", null, error);
                return Result<AuthUser>.Fail(AppError.FromException(code, error));
            }
        }

        /// <summary>Firebase Auth errors the player can act on; anything else is a network-shaped failure.</summary>
        internal static ErrorCode Classify(Exception error)
        {
            var firebase = error as FirebaseException ?? error.InnerException as FirebaseException;
            if (error is AggregateException aggregate)
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                    if (inner is FirebaseException found) firebase = found;
            if (firebase == null) return ErrorCode.NetworkRequired;
            switch ((AuthError)firebase.ErrorCode)
            {
                case AuthError.CredentialAlreadyInUse:
                case AuthError.ProviderAlreadyLinked:
                case AuthError.EmailAlreadyInUse:
                case AuthError.AccountExistsWithDifferentCredentials:
                    return ErrorCode.AccountInUse;
                case AuthError.WebContextCancelled:
                case AuthError.Cancelled:
                    return ErrorCode.Cancelled;
                case AuthError.WebStorageUnsupported:
                    return ErrorCode.Unsupported;
                default:
                    return ErrorCode.NetworkRequired;
            }
        }

        public async Task<Result<AuthUser>> SignIn(LinkProvider provider, CancellationToken cancellation)
        {
            ILinkFlow? flow = null;
            foreach (var candidate in _flows) if (candidate.Provider == provider) flow = candidate;
            if (flow == null) return Result<AuthUser>.Fail(AppError.Of(ErrorCode.Unsupported, ("provider", provider.ToString())));
            try
            {
                var result = await flow.SignIn(_auth, cancellation);
                var user = ToUser(result.User)!;
                _user.Value = user;
                return Result<AuthUser>.Ok(user);
            }
            catch (OperationCanceledException)
            {
                return Result<AuthUser>.Fail(AppError.Of(ErrorCode.Cancelled));
            }
            catch (LinkFlowException error)
            {
                if (error.Code != ErrorCode.Cancelled) _log.Warn("native sign-in failed", null, error);
                return Result<AuthUser>.Fail(AppError.FromException(error.Code, error));
            }
            catch (Exception error)
            {
                var code = Classify(error);
                if (code != ErrorCode.Cancelled) _log.Warn("sign-in failed", null, error);
                return Result<AuthUser>.Fail(AppError.FromException(code, error));
            }
        }

        public System.Collections.Generic.IReadOnlyList<LinkProvider> Linked
        {
            get
            {
                var list = new System.Collections.Generic.List<LinkProvider>();
                var user = _auth.CurrentUser;
                if (user == null) return list;
                foreach (var info in user.ProviderData)
                {
                    if (info.ProviderId == "google.com") list.Add(LinkProvider.Google);
                    else if (info.ProviderId == "apple.com") list.Add(LinkProvider.Apple);
                }
                return list;
            }
        }

        public bool CanLink(LinkProvider provider)
        {
            foreach (var flow in _flows) if (flow.Provider == provider) return true;
            return false;
        }

        public Task SignOut()
        {
            _auth.SignOut();
            _user.Value = null;
            return Task.CompletedTask;
        }

        public void Dispose() => _auth.StateChanged -= OnStateChanged;

        private void OnStateChanged(object sender, EventArgs e) => _main.Post(() => _user.Value = ToUser(_auth.CurrentUser));

        private static AuthUser? ToUser(FirebaseUser? user) => user == null ? null : new AuthUser(user.UserId, user.IsAnonymous);
    }
}
