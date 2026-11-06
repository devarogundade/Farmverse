// The game's calls into the web page around the WebGL build (Assets/WebGLTemplates/Farmverse):
// the page owns the NEAR wallet (sign-in, contract calls) and the browser WebSocket. Answers go back
// to the PageBridge object (Runtime/Infrastructure/Web/PageBridge.cs) through SendMessage.
mergeInto(LibraryManager.library, {
  FvRequestSession: function (id, forceRefresh) {
    var reply = function (ok, value, error) {
      SendMessage('FarmversePage', 'OnReply', JSON.stringify({ id: id, ok: ok, value: value, error: error }));
    };
    if (!window.farmversePage) {
      reply(false, null, 'the page has no wallet (build with the Farmverse WebGL template)');
      return;
    }
    window.farmversePage.requestSession(!!forceRefresh).then(
      function (session) { reply(true, session); },
      function (e) { reply(false, null, String((e && e.message) || e)); }
    );
  },

  FvSignOut: function () {
    if (window.farmversePage) window.farmversePage.signOut();
  },

  FvCallContract: function (id, requestJson) {
    var reply = function (ok, value, error) {
      SendMessage('FarmversePage', 'OnReply', JSON.stringify({ id: id, ok: ok, value: value, error: error }));
    };
    var request;
    try { request = JSON.parse(UTF8ToString(requestJson)); } catch (e) { reply(false, null, 'bad request'); return; }
    if (!window.farmversePage) { reply(false, null, 'the page has no wallet'); return; }
    window.farmversePage.callContract(request).then(
      function (result) { reply(true, result); },
      function (e) { reply(false, null, String((e && e.message) || e)); }
    );
  },

  FvSocketOpen: function (socket, url) {
    var target = UTF8ToString(url);
    var emit = function (kind, data) {
      SendMessage('FarmversePage', 'OnSocket', JSON.stringify({ socket: socket, kind: kind, data: data || '' }));
    };
    window.__fvSockets = window.__fvSockets || {};
    var ws;
    try { ws = new WebSocket(target); } catch (e) { emit('close', String(e)); return; }
    window.__fvSockets[socket] = ws;
    ws.onopen = function () { emit('open'); };
    ws.onmessage = function (e) { if (typeof e.data === 'string') emit('message', e.data); };
    ws.onclose = function (e) { delete window.__fvSockets[socket]; emit('close', e.reason || ''); };
  },

  FvSocketSend: function (socket, text) {
    var ws = (window.__fvSockets || {})[socket];
    if (ws && ws.readyState === 1) ws.send(UTF8ToString(text));
  },

  FvCopyText: function (text) {
    var value = UTF8ToString(text);
    var fallback = function () {
      var area = document.createElement('textarea');
      area.value = value;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      try { document.execCommand('copy'); } catch (e) { }
      document.body.removeChild(area);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(value).catch(fallback);
    else fallback();
  },

  FvReload: function () {
    window.location.reload();
  },

  FvSocketClose: function (socket) {
    var ws = (window.__fvSockets || {})[socket];
    if (!ws) return;
    delete window.__fvSockets[socket];
    ws.onclose = null;
    ws.close();
  },
});
