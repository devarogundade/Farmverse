#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Core.Logging;
using Farmverse.Core.Results;
using Farmverse.Infrastructure.Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.RemoteConfig;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// The session's platform step: run the Firebase boot (App Check, dependency
    /// check, persistence, emulators), then build the Firebase adapters and bind
    /// them behind the deferred ports. Idempotent, so a retry after a failed boot
    /// simply runs again.
    /// </summary>
    internal sealed class FirebaseComposition : IPlatformBootstrap
    {
        private readonly FirebaseEnvironment _environment;
        private readonly FirebasePlatformBootstrap _platform;
        private readonly IMainThread _main;
        private readonly ILog _log;
        private readonly DeferredDocumentStore _documents;
        private readonly DeferredAuth _auth;
        private readonly DeferredRemoteConfig _remoteConfig;
        private bool _bound;

        public FirebaseComposition(FirebaseEnvironment environment, IMainThread main, ILog log,
            DeferredDocumentStore documents, DeferredAuth auth, DeferredRemoteConfig remoteConfig)
        {
            _environment = environment;
            _platform = new FirebasePlatformBootstrap(environment, log);
            _main = main;
            _log = log;
            _documents = documents;
            _auth = auth;
            _remoteConfig = remoteConfig;
        }

        public async Task<Result> Initialize(CancellationToken cancellation)
        {
            var result = await _platform.Initialize(cancellation);
            if (result.IsFailure || _bound) return result;
            _documents.Bind(new FirestoreDocumentStore(FirebaseFirestore.DefaultInstance, _main, _log));
            _auth.Bind(new FirebaseAuthService(FirebaseAuth.DefaultInstance, _main, _log,
                _environment.UsesEmulators ? LocalDevAccount.Load() : null));
            _remoteConfig.Bind(new RemoteConfigService(FirebaseRemoteConfig.DefaultInstance, _environment, _main, _log));
            _bound = true;
            return result;
        }
    }
}
