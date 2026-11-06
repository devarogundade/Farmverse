#nullable enable
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Farmverse.Editor
{
    /// <summary>
    /// The web build (docs 13 §5): the Farmverse WebGL template (wallet sign-in page), Brotli, and the
    /// template's settings per environment. Farmverse ▸ Web ▸ Build (dev | staging | prod).
    /// </summary>
    public static class FarmverseWebBuild
    {
        private const string Template = "PROJECT:Farmverse";

        [MenuItem("Farmverse/Web/Configure WebGL Settings", priority = 40)]
        public static void ConfigureDev() => Configure("http://localhost:8787", "testnet", "http://localhost:5174", "http://localhost:5175");

        [MenuItem("Farmverse/Web/Build (dev, localhost server)", priority = 41)]
        public static void BuildDev() => Build("dev", "http://localhost:8787", "testnet", "http://localhost:5174", "http://localhost:5175");

        [MenuItem("Farmverse/Web/Build (staging, testnet)", priority = 42)]
        public static void BuildStaging() => Build("staging", "https://api.staging.farmverse.world", "testnet", "https://marketplace.staging.farmverse.world", "https://farmverse.world");

        /// <summary>The hackathon build: the real domains, NEAR testnet (also: -executeMethod Farmverse.Editor.FarmverseWebBuild.BuildTestnet).</summary>
        [MenuItem("Farmverse/Web/Build (testnet on farmverse.world)", priority = 42)]
        public static void BuildTestnet() => Build("testnet", "https://api.farmverse.world", "testnet", "https://marketplace.farmverse.world", "https://farmverse.world");

        [MenuItem("Farmverse/Web/Build (prod, mainnet)", priority = 43)]
        public static void BuildProd() => Build("prod", "https://api.farmverse.world", "mainnet", "https://marketplace.farmverse.world", "https://farmverse.world");

        public static void Configure(string apiBaseUrl, string network, string marketplaceUrl, string siteUrl)
        {
            PlayerSettings.WebGL.template = Template;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            // the host serves .br with Content-Encoding: br (see Docs/architecture/13 §5.1)
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.emscriptenArgs = "";
            PlayerSettings.SetTemplateCustomValue("FARMVERSE_API", apiBaseUrl);
            PlayerSettings.SetTemplateCustomValue("NEAR_NETWORK", network);
            PlayerSettings.SetTemplateCustomValue("MARKETPLACE_URL", marketplaceUrl);
            PlayerSettings.SetTemplateCustomValue("SITE_URL", siteUrl);
            // "on": the Email or Google wallet (Privy) once the market's sign page has its Privy app (plan 15 §13.2)
            PlayerSettings.SetTemplateCustomValue("SOCIAL_LOGIN", System.Environment.GetEnvironmentVariable("FARMVERSE_SOCIAL_LOGIN") == "on" ? "on" : "off");
            Debug.Log($"Farmverse: WebGL settings applied (template {Template}, API {apiBaseUrl}, {network}).");
        }

        private static void Build(string environment, string apiBaseUrl, string network, string marketplaceUrl, string siteUrl)
        {
            Configure(apiBaseUrl, network, marketplaceUrl, siteUrl);
            FarmverseWebSize.Apply(); // compressed textures, lighter music and meshes: the download stays well under 100 MB
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("Farmverse: no scenes in the build settings (run Farmverse ▸ Set Up Boot Scene).");
                return;
            }
            var output = Path.Combine("Builds", "WebGL-" + environment);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = environment == "dev" ? BuildOptions.Development : BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"Farmverse: WebGL build ({environment}) in {output}, {report.summary.totalSize / (1024 * 1024)} MB. The boot scene's Environment asset must match ({environment}).");
            else
                Debug.LogError($"Farmverse: WebGL build ({environment}) failed: {report.summary.result}, {report.summary.totalErrors} errors.");
        }
    }
}
