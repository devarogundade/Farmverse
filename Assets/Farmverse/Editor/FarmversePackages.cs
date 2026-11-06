#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Installs the packages the design documents require.
    ///
    /// It goes through the Package Manager API rather than editing
    /// Packages/manifest.json by hand, because the API resolves the version that
    /// suits this editor - a hand-written version string is a guess that fails at
    /// import time with a message about the registry rather than about the
    /// mismatch.
    ///
    /// The API is polled rather than event-driven: Unity 6's requests expose
    /// IsCompleted and Status, and there is no Completed event to subscribe to.
    ///
    /// Run from the menu, or headlessly with
    /// <c>-executeMethod Farmverse.Editor.FarmversePackages.InstallForCI</c>.
    /// The headless path must not use -quit: the install is asynchronous and
    /// -quit would exit before the first package resolved.
    /// </summary>
    public static class FarmversePackages
    {
        /// <summary>
        /// The three the documents effectively require.
        ///
        /// Addressables: Resources is compiled into the build and cannot be
        ///   updated without a store release, which contradicts a game whose
        ///   content is configured remotely.
        /// Localization: the specification ships nine languages including an RTL
        ///   one, and retrofitting that after 81 screens exist is far dearer.
        /// Test framework: the headless checks should also run as Unity tests, and
        ///   only Unity tests can see a screen's real layout.
        /// </summary>
        public static readonly string[] Required =
        {
            "com.unity.addressables",
            "com.unity.localization",
            "com.unity.test-framework",
        };

        private static Queue<string> _queue = new Queue<string>();
        private static AddRequest? _request;
        private static bool _quitWhenDone;
        private static readonly List<string> Installed = new List<string>();
        private static readonly List<string> Failed = new List<string>();

        [MenuItem("Farmverse/Setup/Install Required Packages", priority = 30)]
        public static void InstallFromMenu()
        {
            Begin(false);
        }

        public static void InstallForCI()
        {
            Begin(true);
        }

        private static void Begin(bool quitWhenDone)
        {
            _queue = new Queue<string>(Required);
            _request = null;
            _quitWhenDone = quitWhenDone;
            Installed.Clear();
            Failed.Clear();
            EditorApplication.update += Tick;
            Tick();
        }

        private static void Tick()
        {
            // a request in flight: wait for it
            if (_request != null)
            {
                if (!_request.IsCompleted)
                {
                    return;
                }

                if (_request.Status == StatusCode.Success)
                {
                    Installed.Add($"{_request.Result.packageId}@{_request.Result.version}");
                }
                else
                {
                    Failed.Add($"{_request.Result?.packageId ?? "a package"} " +
                               $"({_request.Error?.message})");
                    Debug.LogWarning($"[Farmverse] a package did not install: " +
                                     $"{_request.Error?.message}");
                }
                _request = null;
            }

            if (_queue.Count > 0)
            {
                _request = Client.Add(_queue.Dequeue());
                return;
            }

            // nothing left in flight and nothing left to ask for
            EditorApplication.update -= Tick;
            Debug.Log($"[Farmverse] packages installed: {Installed.Count} " +
                      $"({string.Join(", ", Installed)})" +
                      (Failed.Count > 0 ? $"; failed: {string.Join(", ", Failed)}" : ""));
            if (_quitWhenDone)
            {
                EditorApplication.Exit(Failed.Count == 0 ? 0 : 1);
            }
        }
    }
}
