#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Lists;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// Ports whose Firebase implementation can only be built after the platform
    /// boot step (dependency check, App Check, emulator wiring). The composition
    /// root hands these to the application immediately and binds the real adapter
    /// once <see cref="FirebaseComposition"/> succeeds. Before that every call is
    /// refused with INTERNAL ("platform not ready"); the session never sees it
    /// because the platform step is its first step.
    /// </summary>
    internal static class NotReady
    {
        public static AppError Error => AppError.Of(ErrorCode.Internal, ("reason", "platform not ready"));
    }

    internal sealed class DeferredDocumentStore : IDocumentStore
    {
        private IDocumentStore? _target;

        public void Bind(IDocumentStore target) => _target = target;

        public IDisposable Listen(string path, Action<DocumentSnapshot> onNext, Action<AppError> onError)
        {
            if (_target != null) return _target.Listen(path, onNext, onError);
            onError(NotReady.Error);
            return Disposable.Empty;
        }

        public Task<Result<DocumentSnapshot>> Get(string path, Freshness freshness, CancellationToken cancellation) =>
            _target?.Get(path, freshness, cancellation) ?? Task.FromResult(Result<DocumentSnapshot>.Fail(NotReady.Error));

        public Task<Result<QuerySnapshot>> Query(QuerySpec query, Freshness freshness, CancellationToken cancellation) =>
            _target?.Query(query, freshness, cancellation) ?? Task.FromResult(Result<QuerySnapshot>.Fail(NotReady.Error));

        public IDisposable ListenQuery(QuerySpec query, Action<QuerySnapshot> onNext, Action<AppError> onError)
        {
            if (_target != null) return _target.ListenQuery(query, onNext, onError);
            onError(NotReady.Error);
            return Disposable.Empty;
        }

        public Task<Result> Update(string path, IReadOnlyDictionary<string, object> fields) =>
            _target?.Update(path, fields) ?? Task.FromResult(Result.Fail(NotReady.Error));
    }

    internal sealed class DeferredAuth : IAuthService
    {
        private readonly ReactiveProperty<AuthUser?> _user = new ReactiveProperty<AuthUser?>(null);
        private IAuthService? _target;
        private IDisposable? _relay;

        /// <summary>Relays the real user into a property that exists from the start, so early subscribers are kept.</summary>
        public IReadOnlyReactiveProperty<AuthUser?> User => _user;

        public void Bind(IAuthService target)
        {
            _target = target;
            _relay?.Dispose();
            _relay = target.User.Subscribe(u => _user.Value = u);
        }

        public Task<Result<AuthUser>> SignInAnonymously(CancellationToken cancellation) =>
            _target?.SignInAnonymously(cancellation) ?? Task.FromResult(Result<AuthUser>.Fail(NotReady.Error));

        public Task SignOut() => _target?.SignOut() ?? Task.CompletedTask;
    }

    internal sealed class DeferredRemoteConfig : IRemoteConfig
    {
        private IRemoteConfig? _target;

        public event Action? Updated;

        public void Bind(IRemoteConfig target)
        {
            _target = target;
            target.Updated += () => Updated?.Invoke();
        }

        public Task<Result> FetchAndActivate(CancellationToken cancellation) =>
            _target?.FetchAndActivate(cancellation) ?? Task.FromResult(Result.Fail(NotReady.Error));

        public string GetString(string key, string fallback) => _target?.GetString(key, fallback) ?? fallback;
        public long GetLong(string key, long fallback) => _target?.GetLong(key, fallback) ?? fallback;
        public bool GetBool(string key, bool fallback) => _target?.GetBool(key, fallback) ?? fallback;
    }
}
