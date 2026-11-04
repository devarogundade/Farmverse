#nullable enable
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// What the iOS plug-ins need in the generated Xcode project, so no build
    /// needs hand edits:
    ///
    ///   * the Sign in with Apple capability (entitlement) on the app target;
    ///   * AuthenticationServices for FarmverseAppleSignIn.mm;
    ///   * Google Sign-In's URL scheme (REVERSED_CLIENT_ID from GoogleService-Info.plist).
    /// </summary>
    public static class FarmverseIosBuild
    {
        private const string Entitlements = "Unity-iPhone/Farmverse.entitlements";

        [PostProcessBuild(200)]
        public static void OnPostprocessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;
            var projectPath = PBXProject.GetPBXProjectPath(buildPath);

            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", false);
            project.WriteToFile(projectPath);

            var capabilities = new ProjectCapabilityManager(projectPath, Entitlements, null, project.GetUnityMainTargetGuid());
            capabilities.AddSignInWithApple();
            capabilities.WriteToFile();

            AddGoogleUrlScheme(buildPath);
        }

        private static void AddGoogleUrlScheme(string buildPath)
        {
            var googlePlistPath = Path.Combine(buildPath, "GoogleService-Info.plist");
            if (!File.Exists(googlePlistPath))
            {
                Debug.LogWarning("Farmverse: GoogleService-Info.plist is not in the Xcode project; Google sign-in will be unavailable.");
                return;
            }
            var google = new PlistDocument();
            google.ReadFromFile(googlePlistPath);
            var reversed = google.root["REVERSED_CLIENT_ID"]?.AsString();
            if (string.IsNullOrEmpty(reversed))
            {
                Debug.LogWarning("Farmverse: GoogleService-Info.plist has no REVERSED_CLIENT_ID; enable Google sign-in in the Firebase console and download it again.");
                return;
            }

            var infoPath = Path.Combine(buildPath, "Info.plist");
            var info = new PlistDocument();
            info.ReadFromFile(infoPath);
            var types = info.root["CFBundleURLTypes"]?.AsArray() ?? info.root.CreateArray("CFBundleURLTypes");
            foreach (var entry in types.values)
            {
                var schemes = entry.AsDict()["CFBundleURLSchemes"]?.AsArray();
                if (schemes == null) continue;
                foreach (var scheme in schemes.values)
                    if (scheme.AsString() == reversed) return;
            }
            var type = types.AddDict();
            type.SetString("CFBundleURLName", "google-sign-in");
            type.CreateArray("CFBundleURLSchemes").AddString(reversed);
            info.WriteToFile(infoPath);
        }
    }
}
#endif
