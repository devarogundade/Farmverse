#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Results;
using Firebase;
using Firebase.AppCheck;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Functions;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// The first boot step (02 §5): App Check provider before anything else talks to
    /// Firebase, then the dependency check, then per-environment wiring — Firestore
    /// offline persistence with a 100 MB cache, or the local emulators in dev (no
    /// App Check, no cache).
    /// </summary>
    public sealed class FirebasePlatformBootstrap : IPlatformBootstrap
    {
        private readonly FirebaseEnvironment _environment;
        private readonly ILog _log;
        /// <summary>Once per run: a game restart (account switch) composes again, but Firebase is set up for good.</summary>
        private static bool _initialized;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRun() => _initialized = false;

        public FirebasePlatformBootstrap(FirebaseEnvironment environment, ILog log)
        {
            _environment = environment;
            _log = log.For("Firebase");
        }

        public async Task<Result> Initialize(CancellationToken cancellation)
        {
            if (_initialized) return Result.Ok();
            try
            {
                // App Check must be configured before the default app is created. The
                // emulators cannot verify attestation (and the backend skips App Check
                // there), so no provider is installed against them.
                if (_environment.UsesEmulators) _log.Info("App Check off: the local emulators do not verify it");
                else if (_environment.AppCheck == AppCheckMode.Debug)
                {
                    if (!string.IsNullOrEmpty(_environment.AppCheckDebugToken)) DebugAppCheckProviderFactory.Instance.SetDebugToken(_environment.AppCheckDebugToken);
                    FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
                }
                else
                {
#if UNITY_IOS
                    FirebaseAppCheck.SetAppCheckProviderFactory(AppAttestProviderFactory.Instance);
#else
                    FirebaseAppCheck.SetAppCheckProviderFactory(PlayIntegrityProviderFactory.Instance);
#endif
                }

                var status = await FirebaseApp.CheckAndFixDependenciesAsync();
                if (status != DependencyStatus.Available)
                {
                    _log.Error("Firebase dependencies unavailable: " + status);
                    return Result.Fail(ErrorCode.Internal, ("reason", status.ToString()));
                }

                var firestore = FirebaseFirestore.DefaultInstance;
                if (_environment.UsesEmulators)
                {
                    // the emulators forget everything they are not told to keep, so a local
                    // cache would serve documents the emulator no longer has
                    var host = _environment.EmulatorHost;
                    firestore.Settings.Host = host + ":" + _environment.FirestoreEmulatorPort;
                    firestore.Settings.SslEnabled = false;
                    firestore.Settings.PersistenceEnabled = false;
                    FirebaseFunctions.GetInstance(FirebaseApp.DefaultInstance, _environment.FunctionsRegion)
                        .UseFunctionsEmulator("http://" + host + ":" + _environment.FunctionsEmulatorPort);
                    var auth = FirebaseAuth.DefaultInstance;
                    auth.UseEmulator(host, _environment.AuthEmulatorPort);
                    // the desktop SDK cannot refresh a saved sign-in through the emulator, so local sessions sign
                    // in afresh every launch (with the same local account: see LocalDevAccount)
                    if (auth.CurrentUser != null) auth.SignOut();
                    _log.Info("using the local emulators at " + host + " (project " + FirebaseApp.DefaultInstance.Options.ProjectId + ")");
                }
                else
                {
                    firestore.Settings.PersistenceEnabled = true;
                    firestore.Settings.CacheSizeBytes = _environment.FirestoreCacheBytes;
                }

                _initialized = true;
                return Result.Ok();
            }
            catch (Exception error)
            {
                _log.Error("Firebase initialization failed", error);
                return Result.Fail(AppError.FromException(ErrorCode.Internal, error));
            }
        }
    }
}
