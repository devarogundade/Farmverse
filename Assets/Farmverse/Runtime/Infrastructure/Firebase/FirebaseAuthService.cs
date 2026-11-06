#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;
using Firebase.Auth;

namespace Farmverse.Infrastructure.Firebase
{
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

    /// <summary>Anonymous sign-in (wallet sign-in replaces it later, 13 §5).</summary>
    public sealed class FirebaseAuthService : IAuthService, IDisposable
    {
        private readonly FirebaseAuth _auth;
        private readonly IMainThread _main;
        private readonly ILog _log;
        private readonly ReactiveProperty<AuthUser?> _user = new ReactiveProperty<AuthUser?>(null);
        private readonly LocalDevAccount? _local;

        public FirebaseAuthService(FirebaseAuth auth, IMainThread main, ILog log, LocalDevAccount? local = null)
        {
            _local = local;
            _auth = auth;
            _main = main;
            _log = log.For("Auth");
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
