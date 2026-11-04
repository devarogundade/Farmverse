#nullable enable
namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>App Check attestation in use (08 §3).</summary>
    public enum AppCheckMode
    {
        /// <summary>Play Integrity on Android, App Attest on iOS.</summary>
        Platform = 0,
        /// <summary>Debug provider with a registered debug token (editor, dev builds).</summary>
        Debug = 1,
    }

    /// <summary>What the Firebase adapters need to know about the environment (built from EnvironmentConfig).</summary>
    public sealed class FirebaseEnvironment
    {
        public string Name { get; set; } = "dev";
        public string FunctionsRegion { get; set; } = "europe-west1";
        public AppCheckMode AppCheck { get; set; } = AppCheckMode.Debug;
        public string AppCheckDebugToken { get; set; } = string.Empty;
        /// <summary>Host of the local emulator suite (e.g. 10.0.2.2 from an Android emulator); empty = real backend.</summary>
        public string EmulatorHost { get; set; } = string.Empty;
        public int FirestoreEmulatorPort { get; set; } = 8080;
        public int FunctionsEmulatorPort { get; set; } = 5001;
        public int AuthEmulatorPort { get; set; } = 9099;
        public long RemoteConfigMinFetchIntervalMs { get; set; } = 5 * 60 * 1000;
        public long FirestoreCacheBytes { get; set; } = 100L * 1024 * 1024;

        public bool UsesEmulators => !string.IsNullOrEmpty(EmulatorHost);
    }
}
