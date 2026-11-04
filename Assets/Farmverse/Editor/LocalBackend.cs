#nullable enable
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// Runs the game against the Firebase emulators on this PC: Farmverse > Local Backend > Start
    /// opens tools/dev/start-backend.cmd (build, emulators, catalogue) in its own window.
    /// The dev environment points at 127.0.0.1, so Play talks to it and never to the real project.
    /// The local farm is kept between runs; Start Fresh begins from an empty backend.
    /// </summary>
    public static class LocalBackend
    {
        [MenuItem("Farmverse/Local Backend/Start", priority = 20)]
        public static void Start() => Run(fresh: false);

        [MenuItem("Farmverse/Local Backend/Start Fresh (empty farm)", priority = 21)]
        public static void StartFresh() => Run(fresh: true);

        private static void Run(bool fresh)
        {
            var root = Path.GetDirectoryName(UnityEngine.Application.dataPath)!;
            var script = Path.Combine(root, "tools", "dev", "start-backend.cmd");
            if (!File.Exists(script))
            {
                UnityEngine.Debug.LogError("Farmverse: " + script + " is missing.");
                return;
            }
            Process.Start(new ProcessStartInfo("cmd.exe", "/c start \"Farmverse local backend\" \"" + script + "\"" + (fresh ? " --fresh" : ""))
            {
                UseShellExecute = true,
                WorkingDirectory = root,
            });
            UnityEngine.Debug.Log("Farmverse: starting the local backend in its own window (emulator UI at http://127.0.0.1:4000).");
        }

        /// <summary>
        /// Runs tools/dev/e2e-money.mjs (every server module against the running local backend) and writes the
        /// result to the Console and to "Claude outputs/e2e-result.txt".
        /// </summary>
        [MenuItem("Farmverse/Local Backend/Run End-to-End Check", priority = 23)]
        public static void EndToEnd()
        {
            var root = Path.GetDirectoryName(UnityEngine.Application.dataPath)!;
            var output = Path.Combine(root, "Claude outputs", "e2e-result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var info = new ProcessStartInfo("cmd.exe", "/c node tools\\dev\\e2e-money.mjs > \"" + output + "\" 2>&1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
            var process = Process.Start(info);
            UnityEngine.Debug.Log("Farmverse: end-to-end check running…");
            if (process == null) return;
            process.EnableRaisingEvents = true;
            process.Exited += (_, __) =>
            {
                var text = File.Exists(output) ? File.ReadAllText(output) : "(no output)";
                var passed = text.Contains("ALL PASSED");
                EditorApplication.delayCall += () =>
                {
                    if (passed) UnityEngine.Debug.Log("Farmverse: end-to-end check ALL PASSED\n" + text);
                    else UnityEngine.Debug.LogError("Farmverse: end-to-end check FAILED\n" + text);
                };
            };
        }

        /// <summary>(Re)start only the island engine (port 8090) in its own window, e.g. after it failed to build.</summary>
        [MenuItem("Farmverse/Local Backend/Start Island Engine", priority = 24)]
        public static void StartEngine()
        {
            var root = Path.GetDirectoryName(UnityEngine.Application.dataPath)!;
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c start \"Farmverse island engine\" cmd /k \"set PORT=8090&& dotnet run --project tools\\dotnet\\Farmverse.IslandEngine -c Release\"")
            {
                UseShellExecute = true,
                WorkingDirectory = root,
            });
            UnityEngine.Debug.Log("Farmverse: starting the island engine on port 8090 in its own window.");
        }

        /// <summary>Mail every local player a welcome message with 100 coins and 5 gems (tools/sendMail.ts, emulator only).</summary>
        [MenuItem("Farmverse/Local Backend/Send Test Mail (all local players)", priority = 25)]
        public static void SendTestMail()
        {
            var root = Path.GetDirectoryName(UnityEngine.Application.dataPath)!;
            var info = new ProcessStartInfo("cmd.exe",
                "/c set FIRESTORE_EMULATOR_HOST=127.0.0.1:8080&& npm run mail:send --workspace @farmverse/functions -- --project farmverse-8deb6 --all --title \"Welcome, farmer!\" --body \"A gift from the team.\" --coins 100 --gems 5")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root,
            };
            var process = Process.Start(info);
            if (process == null) return;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            UnityEngine.Debug.Log("Farmverse: test mail — " + output.Trim());
        }

        /// <summary>Publish contracts/src/catalogue.ts (prices, rewards, ads…) into the running emulator.</summary>
        [MenuItem("Farmverse/Local Backend/Publish Catalogue", priority = 26)]
        public static void PublishCatalogue()
        {
            var root = Path.GetDirectoryName(UnityEngine.Application.dataPath)!;
            var info = new ProcessStartInfo("cmd.exe",
                "/c set FIRESTORE_EMULATOR_HOST=127.0.0.1:8080&& npm run catalogue:publish --workspace @farmverse/functions -- --project farmverse-8deb6")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root,
            };
            var process = Process.Start(info);
            if (process == null) return;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            UnityEngine.Debug.Log("Farmverse: catalogue — " + output.Trim());
        }

        [MenuItem("Farmverse/Local Backend/Open Emulator UI", priority = 22)]
        public static void OpenUi() => UnityEngine.Application.OpenURL("http://127.0.0.1:4000");
    }
}
