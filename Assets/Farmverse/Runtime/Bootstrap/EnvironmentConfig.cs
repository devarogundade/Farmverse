#nullable enable
using Farmverse.Core.Logging;
using Farmverse.Infrastructure.Firebase;
using UnityEngine;

namespace Farmverse.Bootstrap
{
    /// <summary>
    /// One asset per environment (dev, staging, prod; 10 §2). The build pipeline
    /// assigns the right one to the boot scene; nothing else branches on environment.
    /// </summary>
    [CreateAssetMenu(menuName = "Farmverse/Environment Config", fileName = "Environment.dev")]
    public sealed class EnvironmentConfig : ScriptableObject
    {
        [SerializeField] private string environmentName = "dev";
        [SerializeField] private string functionsRegion = "europe-west1";
        [SerializeField] private AppCheckMode appCheck = AppCheckMode.Debug;
        [Tooltip("Empty for the real project; 127.0.0.1 (or the LAN IP for devices) to use the emulators.")]
        [SerializeField] private string emulatorHost = string.Empty;
        [SerializeField] private LogLevel minimumLog = LogLevel.Debug;

        public LogLevel MinimumLog => minimumLog;

        public FirebaseEnvironment ToFirebase() => new FirebaseEnvironment
        {
            Name = environmentName,
            FunctionsRegion = functionsRegion,
            AppCheck = appCheck,
            AppCheckDebugToken = DebugToken(),
            EmulatorHost = emulatorHost,
        };

        /// <summary>
        /// The App Check debug token is a secret, so it never lives in this asset
        /// (assets are committed). In the editor it is read from the
        /// FARMVERSE_APPCHECK_DEBUG_TOKEN environment variable or from
        /// UserSettings/appcheck-debug-token.txt (UserSettings is not committed).
        /// Device debug builds print their own token to the log on first run;
        /// register that one in the Firebase console.
        /// </summary>
        private string DebugToken()
        {
#if UNITY_EDITOR
            if (appCheck != AppCheckMode.Debug) return string.Empty;
            var fromEnv = System.Environment.GetEnvironmentVariable("FARMVERSE_APPCHECK_DEBUG_TOKEN");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
            const string file = "UserSettings/appcheck-debug-token.txt";
            return System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : string.Empty;
#else
            return string.Empty;
#endif
        }

        private void OnValidate()
        {
            if (environmentName == "prod" && (appCheck == AppCheckMode.Debug || !string.IsNullOrEmpty(emulatorHost)))
                Debug.LogError("Environment.prod must use the platform App Check provider and no emulators (08 §3.1).", this);
        }
    }
}
