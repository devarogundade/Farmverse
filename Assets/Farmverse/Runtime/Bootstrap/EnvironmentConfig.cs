#nullable enable
using Farmverse.Core.Logging;
using Farmverse.Infrastructure.Web;
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
        [Tooltip("The game server (backend/server), no trailing slash: http://localhost:8787 locally.")]
        [SerializeField] private string apiBaseUrl = "http://localhost:8787";
        [Tooltip("Editor/desktop only: the NEAR account the dev sign-in plays as (the server must run with FARMVERSE_ENV=dev).")]
        [SerializeField] private string devAccount = "dev-farmer.testnet";
        [SerializeField] private LogLevel minimumLog = LogLevel.Debug;

        public string Name => environmentName;
        public LogLevel MinimumLog => minimumLog;

        public WebEnvironment ToWeb() => new WebEnvironment
        {
            ApiBaseUrl = apiBaseUrl.TrimEnd('/'),
            DevAccount = DevAccount(),
        };

        /// <summary>
        /// The editor's dev account: FARMVERSE_DEV_ACCOUNT, else UserSettings/dev-near-account.txt
        /// (not committed), else the asset's value.
        /// </summary>
        private string DevAccount()
        {
#if UNITY_EDITOR
            var fromEnv = System.Environment.GetEnvironmentVariable("FARMVERSE_DEV_ACCOUNT");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
            const string file = "UserSettings/dev-near-account.txt";
            if (System.IO.File.Exists(file)) return System.IO.File.ReadAllText(file).Trim();
#endif
            return devAccount;
        }

        private void OnValidate()
        {
            if (environmentName != "dev" && apiBaseUrl.StartsWith("http://", System.StringComparison.Ordinal))
                Debug.LogError($"Environment.{environmentName} must reach the server over https.", this);
        }
    }
}
