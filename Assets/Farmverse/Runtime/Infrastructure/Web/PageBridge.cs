#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Farmverse.Contracts;
using UnityEngine;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>
    /// The web page around the WebGL build (Assets/WebGLTemplates/Farmverse) owns the NEAR wallet:
    /// it signs the player in (NEP-413 → session tokens) and signs contract calls. This is the game's
    /// side of that page API (Plugins/WebGL/FarmversePage.jslib); answers come back through
    /// <c>SendMessage</c> to this object, matched by request id.
    /// </summary>
    public sealed class PageBridge : MonoBehaviour
    {
        public const string ObjectName = "FarmversePage";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void FvRequestSession(int id, int forceRefresh);
        [DllImport("__Internal")] private static extern void FvSignOut();
        [DllImport("__Internal")] private static extern void FvCallContract(int id, string requestJson);
        [DllImport("__Internal")] private static extern void FvSocketOpen(int socket, string url);
        [DllImport("__Internal")] private static extern void FvSocketSend(int socket, string text);
        [DllImport("__Internal")] private static extern void FvSocketClose(int socket);
#else
        private static void FvRequestSession(int id, int forceRefresh) => throw new PlatformNotSupportedException("the page bridge exists only in WebGL builds");
        private static void FvSignOut() { }
        private static void FvCallContract(int id, string requestJson) => throw new PlatformNotSupportedException("the page bridge exists only in WebGL builds");
        private static void FvSocketOpen(int socket, string url) => throw new PlatformNotSupportedException("the page bridge exists only in WebGL builds");
        private static void FvSocketSend(int socket, string text) { }
        private static void FvSocketClose(int socket) { }
#endif

        private static PageBridge? _instance;
        private readonly Dictionary<int, TaskCompletionSource<string>> _pending = new Dictionary<int, TaskCompletionSource<string>>();
        private readonly Dictionary<int, BrowserSocket> _sockets = new Dictionary<int, BrowserSocket>();
        private int _next = 1;

        /// <summary>The one bridge object (created on first use, kept across scenes).</summary>
        public static PageBridge Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject(ObjectName);
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<PageBridge>();
                return _instance;
            }
        }

        /// <summary>
        /// The page's session as JSON (<c>{accessToken, accessExpiresAt, uid, accountId}</c>): the page
        /// shows its "Connect wallet" flow if needed, and refreshes an expiring token itself.
        /// </summary>
        public Task<string> RequestSession(bool forceRefresh)
        {
            var (id, task) = Begin();
            FvRequestSession(id, forceRefresh ? 1 : 0);
            return task;
        }

        public void SignOut() => FvSignOut();

        /// <summary>
        /// Asks the wallet to sign and send a function call. <paramref name="requestJson"/>:
        /// <c>{contractId, method, args, deposit (yocto string), gasTgas}</c>; answers <c>{txHash}</c> JSON.
        /// </summary>
        public Task<string> CallContract(string requestJson)
        {
            var (id, task) = Begin();
            FvCallContract(id, requestJson);
            return task;
        }

        internal int OpenSocket(BrowserSocket socket, string url)
        {
            var id = _next++;
            _sockets[id] = socket;
            FvSocketOpen(id, url);
            return id;
        }

        internal void SendSocket(int id, string text) => FvSocketSend(id, text);

        internal void CloseSocket(int id)
        {
            _sockets.Remove(id);
            FvSocketClose(id);
        }

        private (int, Task<string>) Begin()
        {
            var id = _next++;
            var tcs = new TaskCompletionSource<string>();
            _pending[id] = tcs;
            return (id, tcs.Task);
        }

        // ---- called by the page through SendMessage (one string argument each) ----------------------

        /// <summary><c>{id, ok, value | error}</c>: the answer to a request.</summary>
        public void OnReply(string json)
        {
            if (WireJson.Parse(json) is not Dictionary<string, object> m) return;
            var id = m.TryGetValue("id", out var i) && i is long l ? (int)l : 0;
            if (!_pending.TryGetValue(id, out var tcs)) return;
            _pending.Remove(id);
            if (m.TryGetValue("ok", out var ok) && ok is bool yes && yes)
                tcs.TrySetResult(m.TryGetValue("value", out var v) ? WireJson.Serialize(v) : "null");
            else
                tcs.TrySetException(new PageException(m.TryGetValue("error", out var e) ? e as string ?? "page error" : "page error"));
        }

        /// <summary><c>{socket, kind: open|message|close, data}</c>: a browser WebSocket event.</summary>
        public void OnSocket(string json)
        {
            if (WireJson.Parse(json) is not Dictionary<string, object> m) return;
            var id = m.TryGetValue("socket", out var s) && s is long l ? (int)l : 0;
            if (!_sockets.TryGetValue(id, out var socket)) return;
            var data = m.TryGetValue("data", out var d) ? d as string ?? string.Empty : string.Empty;
            switch (m.TryGetValue("kind", out var k) ? k as string : null)
            {
                case "open": socket.RaiseOpened(); break;
                case "message": socket.RaiseMessage(data); break;
                case "close":
                    _sockets.Remove(id);
                    socket.RaiseClosed(data);
                    break;
            }
        }
    }

    /// <summary>The page refused or failed (wallet closed, user rejected, network).</summary>
    public sealed class PageException : Exception
    {
        public PageException(string message) : base(message) { }

        /// <summary>The player closed the wallet or rejected the request.</summary>
        public bool Rejected => Message.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0
            || Message.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
