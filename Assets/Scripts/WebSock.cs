using System;
using System.Collections.Generic;
using UnityEngine;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#else
using System.Runtime.InteropServices;
#endif

/// <summary>
/// One WebSocket connection. Browser build (itch.io): the page's own WebSocket through RRSocket.jslib.
/// Everything else (Windows / Mac / Android...): System.Net.WebSockets on background tasks.
/// Poll() on the main thread returns the received text messages.
/// </summary>
public class WebSock
{
    public bool Failed;                  // could not connect / connection broke
    public bool IsOpen { get { return state == 2; } }
    public bool IsClosed { get { return state == 0 || Failed; } }
    int state;                           // 1 connecting, 2 open, 0 closed

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void RRS_Open(string url);
    [DllImport("__Internal")] static extern int RRS_State();
    [DllImport("__Internal")] static extern void RRS_Send(string msg);
    [DllImport("__Internal")] static extern void RRS_Close();
    [DllImport("__Internal")] static extern IntPtr RRS_Poll();
    [DllImport("__Internal")] static extern void RRS_Free(IntPtr p);

    public void Connect(string url) { Failed = false; state = 1; RRS_Open(url); }
    public void Send(string s) { RRS_Send(s); }
    public void Close() { RRS_Close(); state = 0; }
    public string Poll()
    {
        int s = RRS_State();
        if (s == -1) { Failed = true; state = 0; }
        else state = s;
        IntPtr p = RRS_Poll();
        if (p == IntPtr.Zero) return null;
        string r = Marshal.PtrToStringUTF8(p);
        RRS_Free(p);
        return r;
    }
#else
    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly Queue<string> inbox = new Queue<string>();
    readonly Queue<string> outbox = new Queue<string>();

    public void Connect(string url)
    {
        Failed = false; state = 1;
        cts = new CancellationTokenSource();
        ws = new ClientWebSocket();
        var token = cts.Token; var sock = ws;
        Task.Run(async () =>
        {
            try
            {
                await sock.ConnectAsync(new Uri(url), token);
                state = 2;
                var rx = ReceiveLoop(sock, token);
                var tx = SendLoop(sock, token);
                await Task.WhenAny(rx, tx);
            }
            catch (Exception) { Failed = true; }
            state = 0;
            try { sock.Dispose(); } catch (Exception) { }
        });
    }

    async Task ReceiveLoop(ClientWebSocket sock, CancellationToken token)
    {
        var buf = new byte[16384];
        var sb = new StringBuilder();
        while (sock.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var res = await sock.ReceiveAsync(new ArraySegment<byte>(buf), token);
            if (res.MessageType == WebSocketMessageType.Close) return;
            sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            if (res.EndOfMessage) { lock (inbox) inbox.Enqueue(sb.ToString()); sb.Length = 0; }
        }
    }

    async Task SendLoop(ClientWebSocket sock, CancellationToken token)
    {
        while (sock.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            string m = null;
            lock (outbox) { if (outbox.Count > 0) m = outbox.Dequeue(); }
            if (m == null) { await Task.Delay(8, token); continue; }
            var bytes = Encoding.UTF8.GetBytes(m);
            await sock.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
    }

    public void Send(string s) { lock (outbox) { if (outbox.Count < 200) outbox.Enqueue(s); } }

    public void Close()
    {
        state = 0;
        try { if (cts != null) cts.Cancel(); } catch (Exception) { }
    }

    public string Poll()
    {
        lock (inbox) { return inbox.Count > 0 ? inbox.Dequeue() : null; }
    }
#endif
}
