#nullable enable
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>Where the game server lives (docs 13 §5).</summary>
    public sealed class WebEnvironment
    {
        /// <summary>e.g. <c>https://api.farmverse.world</c> or <c>http://localhost:8787</c> (no trailing slash).</summary>
        public string ApiBaseUrl = "http://localhost:8787";
        /// <summary>Editor only: the NEAR account the dev sign-in plays as (server must run FARMVERSE_ENV=dev).</summary>
        public string DevAccount = "dev-farmer.testnet";

        /// <summary>The live channel: the API URL with ws/wss.</summary>
        public string LiveUrl =>
            (ApiBaseUrl.StartsWith("https://", StringComparison.Ordinal) ? "wss://" + ApiBaseUrl.Substring(8) : "ws://" + ApiBaseUrl.Substring(ApiBaseUrl.IndexOf("://", StringComparison.Ordinal) + 3)) + "/v1/live";
    }

    /// <summary>An HTTP answer: status 0 means the request never reached the server.</summary>
    public readonly struct WebResponse
    {
        public WebResponse(long status, string body, string? error)
        {
            Status = status;
            Body = body;
            Error = error;
        }

        public long Status { get; }
        public string Body { get; }
        public string? Error { get; }
        public bool Reached => Status > 0;
        public bool Ok => Status >= 200 && Status < 300;
    }

    /// <summary>
    /// JSON over HTTP with UnityWebRequest (works in WebGL, where there are no threads). Must be
    /// called on the main thread; awaits resume there.
    /// </summary>
    public sealed class WebApi
    {
        private readonly string _base;

        public WebApi(WebEnvironment environment) => _base = environment.ApiBaseUrl.TrimEnd('/');

        public string Url(string path) => _base + path;

        public async Task<WebResponse> Send(string method, string path, string? json, string? bearer, TimeSpan timeout, CancellationToken cancellation)
        {
            using var request = new UnityWebRequest(Url(path), method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)),
            };
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);

            var done = new TaskCompletionSource<bool>();
            using (cancellation.Register(() => { request.Abort(); done.TrySetCanceled(); }))
            {
                request.SendWebRequest().completed += _ => done.TrySetResult(true);
                await done.Task;
            }
            var status = request.responseCode;
            var body = request.downloadHandler?.text ?? string.Empty;
            if (request.result == UnityWebRequest.Result.ConnectionError) return new WebResponse(0, body, request.error);
            return new WebResponse(status, body, request.result == UnityWebRequest.Result.Success ? null : request.error);
        }
    }
}
