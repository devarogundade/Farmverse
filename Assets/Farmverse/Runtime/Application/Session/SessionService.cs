#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Config;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Application.Remote;
using Farmverse.Contracts;
using Farmverse.Core.Lists;
using Farmverse.Core.Logging;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;
using Farmverse.Core.Scheduling;

namespace Farmverse.Application.Session
{
    public enum SessionState
    {
        Starting = 0,
        InitPlatform,
        CheckingVersion,
        Maintenance,
        UpdateRequired,
        Authenticating,
        LoadingPlayer,
        Onboarding,
        Ready,
        Suspended,
        Offline,
        Failed,
    }

    public sealed class SessionDependencies
    {
        public IPlatformBootstrap Platform { get; set; } = null!;
        public IRemoteConfig RemoteConfig { get; set; } = null!;
        public IAuthService Auth { get; set; } = null!;
        public IDocumentStore Documents { get; set; } = null!;
        public IConnectivity Connectivity { get; set; } = null!;
        public IAppInfo App { get; set; } = null!;
        public ICrashReporter Crash { get; set; } = null!;
        public CallableClient Callables { get; set; } = null!;
        public IScheduler Scheduler { get; set; } = null!;
        public ILog Log { get; set; } = null!;
    }

    /// <summary>
    /// The boot state machine (02 §4.2): platform, version gate, sign-in, the player document
    /// (created on first run with a free generated name), then Ready. The game plays offline
    /// meanwhile; server features wait for Ready. Each step has a timeout; a
    /// transient failure is retried with backoff, anything else stops in
    /// <see cref="SessionState.Failed"/> with an <see cref="AppError"/> the UI maps
    /// to a screen.
    /// </summary>
    public sealed class SessionService
    {
        public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);
        public const string DefaultAvatar = "avatar_default";
        private static readonly TimeSpan[] Backoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8) };

        private readonly SessionDependencies _d;
        private readonly ILog _log;
        private readonly ReactiveProperty<SessionState> _state = new ReactiveProperty<SessionState>(SessionState.Starting);

        public SessionService(SessionDependencies dependencies)
        {
            _d = dependencies;
            _log = dependencies.Log.For("Session");
        }

        public IReadOnlyReactiveProperty<SessionState> State => _state;

        public AppError? Error { get; private set; }

        public string? Uid { get; private set; }

        /// <summary>Run the boot from the start. Safe to call again after <see cref="SessionState.Failed"/>.</summary>
        public async Task Start(CancellationToken cancellation)
        {
            Error = null;
            try
            {
                await Step(SessionState.InitPlatform, ct => _d.Platform.Initialize(ct), cancellation);

                Enter(SessionState.CheckingVersion);
                await _d.RemoteConfig.FetchAndActivate(cancellation); // failure keeps defaults/cached values
                if (_d.RemoteConfig.GetBool(RemoteKeys.Maintenance, RemoteKeys.MaintenanceDefault))
                {
                    Stop(SessionState.Maintenance, AppError.Of(ErrorCode.Maintenance));
                    return;
                }
                var minimum = _d.RemoteConfig.GetString(RemoteKeys.MinClientVersion, RemoteKeys.MinClientVersionDefault);
                if (CompareVersions(_d.App.Version, minimum) < 0)
                {
                    Stop(SessionState.UpdateRequired, AppError.Of(ErrorCode.ClientOutdated, ("minVersion", minimum)));
                    return;
                }

                Enter(SessionState.Authenticating);
                var user = _d.Auth.User.Value;
                if (user == null)
                {
                    if (!_d.Connectivity.IsOnline.Value)
                    {
                        Stop(SessionState.Offline, AppError.Of(ErrorCode.NetworkRequired));
                        return;
                    }
                    var signedIn = await WithRetry(ct => _d.Auth.SignInAnonymously(ct), cancellation);
                    user = signedIn;
                }
                Uid = user.Uid;
                _d.Crash.SetUser(user.Uid);

                Enter(SessionState.LoadingPlayer);
                var player = await WithRetry(ct => _d.Documents.Get(Paths.Player(user.Uid), Freshness.Default, ct), cancellation);
                if (!player.Exists)
                {
                    Enter(SessionState.Onboarding);
                    await WithRetry(CreatePlayer, cancellation);
                }

                Enter(SessionState.Ready);
            }
            catch (OperationCanceledException)
            {
                _log.Info("boot cancelled");
            }
            catch (SessionStepFailed failure)
            {
                Stop(SessionState.Failed, failure.Error);
            }
        }

        /// <summary>
        /// First run: create the player under a free farm name. The server suggests names nobody has taken;
        /// a name taken in between is simply the next one tried.
        /// </summary>
        private async Task<Result<bool>> CreatePlayer(CancellationToken cancellation)
        {
            var names = await _d.Callables.Call(Callables.PlayerSuggestNames, new PlayerSuggestNamesRequest { Meta = Meta(), Count = 5 }, cancellation);
            if (names.IsFailure) return Result<bool>.Fail(names.Error);
            foreach (var name in names.Value.Names)
            {
                var request = new PlayerCreateRequest { Meta = Meta(), DisplayName = name, AvatarId = DefaultAvatar };
                var created = await _d.Callables.Call(Callables.PlayerCreate, request, cancellation);
                if (created.IsOk || created.Error.Code == ErrorCode.PlayerExists) return Result<bool>.Ok(true);
                if (created.Error.Code != ErrorCode.NameTaken) return Result<bool>.Fail(created.Error);
            }
            return Result<bool>.Fail(AppError.Of(ErrorCode.Conflict));   // every suggestion went meanwhile: transient, retried
        }

        /// <summary>App paused.</summary>
        public void Pause()
        {
            if (_state.Value == SessionState.Ready) Enter(SessionState.Suspended);
        }

        /// <summary>App resumed.</summary>
        public void Resume()
        {
            if (_state.Value == SessionState.Suspended) Enter(SessionState.Ready);
        }

        public RequestMeta Meta() => new RequestMeta { ClientVersion = _d.App.Version, Platform = _d.App.Platform, Locale = _d.App.Locale };

        private async Task Step(SessionState state, Func<CancellationToken, Task<Result>> run, CancellationToken cancellation)
        {
            Enter(state);
            await WithRetry(async ct =>
            {
                var result = await run(ct);
                return result.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(result.Error);
            }, cancellation);
        }

        private async Task<T> WithRetry<T>(Func<CancellationToken, Task<Result<T>>> run, CancellationToken cancellation)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(StepTimeout);
                Result<T> result;
                try
                {
                    result = await run(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    result = Result<T>.Fail(AppError.Of(ErrorCode.Timeout));
                }
                if (result.IsOk) return result.Value;
                if (!IsTransient(result.Error.Code) || attempt >= Backoff.Length) throw new SessionStepFailed(result.Error);
                await _d.Scheduler.Delay(Backoff[attempt], cancellation);
            }
        }

        private static bool IsTransient(ErrorCode code) =>
            code == ErrorCode.NetworkRequired || code == ErrorCode.Timeout || code == ErrorCode.Internal || code == ErrorCode.RateLimited || code == ErrorCode.Conflict;

        private void Enter(SessionState state)
        {
            _log.Info("session " + state);
            _d.Crash.Breadcrumb("session:" + state);
            _state.Value = state;
        }

        private void Stop(SessionState state, AppError error)
        {
            Error = error;
            _log.Warn("session stopped in " + state, new Dictionary<string, object?> { ["code"] = error.Code.ToString() });
            Enter(state);
        }

        /// <summary>Semantic versions compared numerically, major.minor.patch.</summary>
        public static int CompareVersions(string a, string b)
        {
            var x = a.Split('.');
            var y = b.Split('.');
            for (var i = 0; i < 3; i++)
            {
                var p = i < x.Length && int.TryParse(x[i], out var xi) ? xi : 0;
                var q = i < y.Length && int.TryParse(y[i], out var yi) ? yi : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        private sealed class SessionStepFailed : Exception
        {
            public SessionStepFailed(AppError error) : base(error.ToString())
            {
                Error = error;
            }

            public AppError Error { get; }
        }
    }
}
