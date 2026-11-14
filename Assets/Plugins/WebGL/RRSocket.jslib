mergeInto(LibraryManager.library, {
  // Tiny WebSocket bridge for the co-op server (works on itch.io: wss:// only).
  RRS_Open: function (urlPtr) {
    var S = window.__rrs || (window.__rrs = { ws: null, q: [], state: 0, err: 0 });
    try { if (S.ws) S.ws.close(); } catch (e) {}
    S.q = []; S.state = 1; S.err = 0;
    try {
      var ws = new WebSocket(UTF8ToString(urlPtr));
      S.ws = ws;
      ws.onopen = function () { S.state = 2; };
      ws.onmessage = function (e) { if (typeof e.data === 'string') S.q.push(e.data); };
      ws.onclose = function () { S.state = 0; };
      ws.onerror = function () { S.err = 1; };
    } catch (e) { S.state = 0; S.err = 1; }
  },
  RRS_State: function () { var S = window.__rrs; return S ? (S.err && S.state !== 2 ? -1 : S.state) : 0; },
  RRS_Send: function (ptr) { var S = window.__rrs; if (S && S.ws && S.state === 2) { try { S.ws.send(UTF8ToString(ptr)); } catch (e) {} } },
  RRS_Close: function () { var S = window.__rrs; if (S && S.ws) { try { S.ws.close(); } catch (e) {} S.state = 0; } },
  RRS_Poll: function () {
    var S = window.__rrs;
    if (!S || S.q.length === 0) return 0;
    var s = S.q.shift();
    var n = lengthBytesUTF8(s) + 1;
    var p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },
  RRS_Free: function (p) { _free(p); }
});
