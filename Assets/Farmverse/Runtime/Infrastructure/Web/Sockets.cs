#nullable enable
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;

namespace Farmverse.Infrastructure.Web
{
    /// <summary>A text WebSocket whose events arrive on the main thread.</summary>
    public interface ITextSocket
    {
        event Action? Opened;
        event Action<string>? Message;
        /// <summary>Closed or failed (the reason, may be empty). Not raised after <see cref="Close"/>.</summary>
        event Action<string>? Closed;
        void Connect(string url);
        void Send(string text);
        void Close();
    }

    /// <summary>The browser's WebSocket, through the page bridge (WebGL).</summary>
    public sealed class BrowserSocket : ITextSocket
    {
        private int _id;
        public event Action? Opened;
        public event Action<string>? Message;
        public event Action<string>? Closed;

        public void Connect(string url) => _id = PageBridge.Instance.OpenSocket(this, url);
        public void Send(string text)
        {
            if (_id != 0) PageBridge.Instance.SendSocket(_id, text);
        }
        public void Close()
        {
            if (_id == 0) return;
            PageBridge.Instance.CloseSocket(_id);
            _id = 0;
        }

        internal void RaiseOpened() => Opened?.Invoke();
        internal void RaiseMessage(string text) => Message?.Invoke(text);
        internal void RaiseClosed(string reason)
        {
            _id = 0;
            Closed?.Invoke(reason);
        }
    }

    /// <summary>.NET's ClientWebSocket (editor, desktop); its events are posted to the main thread.</summary>
    public sealed class DotNetSocket : ITextSocket
    {
        private readonly IMainThread _main;
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _life;
        private Task _sending = Task.CompletedTask;

        public DotNetSocket(IMainThread main) => _main = main;

        public event Action? Opened;
        public event Action<string>? Message;
        public event Action<string>? Closed;

        public void Connect(string url)
        {
            Close();
            var ws = new ClientWebSocket();
            var life = new CancellationTokenSource();
            _ws = ws;
            _life = life;
            _ = Run(ws, new Uri(url), life.Token);
        }

        public void Send(string text)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
            // ClientWebSocket allows one send at a time: chain them
            _sending = _sending.ContinueWith(_ => ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)).Unwrap();
        }

        public void Close()
        {
            var ws = _ws;
            _ws = null;
            _life?.Cancel();
            _life = null;
            if (ws == null) return;
            try { ws.Abort(); } catch { /* already gone */ }
            ws.Dispose();
        }

        private async Task Run(ClientWebSocket ws, Uri url, CancellationToken token)
        {
            string reason = string.Empty;
            try
            {
                await ws.ConnectAsync(url, token).ConfigureAwait(false);
                _main.Post(() => { if (_ws == ws) Opened?.Invoke(); });
                var buffer = new byte[16 * 1024];
                var text = new StringBuilder();
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var got = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (got.MessageType == WebSocketMessageType.Close)
                    {
                        reason = ws.CloseStatusDescription ?? string.Empty;
                        break;
                    }
                    text.Append(Encoding.UTF8.GetString(buffer, 0, got.Count));
                    if (!got.EndOfMessage) continue;
                    var message = text.ToString();
                    text.Clear();
                    _main.Post(() => { if (_ws == ws) Message?.Invoke(message); });
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                reason = e.Message;
            }
            catch (OperationCanceledException)
            {
                return; // closed by us
            }
            _main.Post(() =>
            {
                if (_ws != ws) return;
                _ws = null;
                Closed?.Invoke(reason);
            });
        }
    }
}
