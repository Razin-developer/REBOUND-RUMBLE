using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

[Serializable] public class CoopPlayer { public string user; public int suit; public bool host; }
[Serializable] public class CoopFriend { public string user; public bool online; public string room; }
[Serializable] public class CoopName { public string user; }
[Serializable]
public class CoopMsg
{
    public string t, msg, code, user, from, d, role, why, db;
    public int suit;
    public bool started;
    public CoopPlayer[] players;
    public CoopFriend[] friends;
    public CoopName[] requests, sent;
}

public enum CoopConn { Off, Connecting, Online }
public enum CoopRole { None, Host, Guest }

/// <summary>
/// 2-player online co-op.
///   * One WebSocket to the multiplayer server (accounts, friends, rooms, relay).
///   * The HOST's game is the real game. The GUEST sends its position + button state, and gets back snapshots
///     (enemies, rings, hearts, score...) that it draws as ghosts. Hearts, score and kills are shared.
///   * Host rings are gold, guest rings are blue.
/// </summary>
public class Coop : MonoBehaviour
{
    // ------------------------------------------------------------------ EDIT THIS after you deploy on Render:
    public const string ServerUrl = "wss://rebound-rumble-multiplayer.onrender.com";

    // ------------------------------------------------------------------ public state (read by the UI)
    public static CoopConn Conn;
    public static CoopRole Role;
    public static bool InRun, PendingStart, WantLobby;
    public static string RoomCode = "", Info = "", Err = "";
    public static readonly List<CoopPlayer> Members = new List<CoopPlayer>();
    public static readonly List<CoopFriend> Friends = new List<CoopFriend>();
    public static readonly List<string> Requests = new List<string>();
    public static readonly List<string> Sent = new List<string>();
    public static string InviteFrom = "", InviteCode = "";
    public static float InviteUntil;
    public static float GuestMoveMul = 1f;

    public static bool IsGuest { get { return InRun && Role == CoopRole.Guest; } }
    public static bool IsHost { get { return InRun && Role == CoopRole.Host; } }
    public static bool HasAvatar { get { return IsHost && avatar != null; } }
    public static int ExtraCap { get { return HasAvatar ? 6 : 0; } }
    public static bool InRoom { get { return RoomCode.Length > 0; } }
    public static float ConnectingFor { get { return Time.unscaledTime - connectStart; } }

    // ------------------------------------------------------------------ internals
    static WebSock sock;
    static float connectStart, lastPing, lastSnap, lastW, lastGuestSend, lastHostSend, lastHostThrow;
    static bool helloSent, skipLeave, remoteEnd;
    static int myRid = -1;
    static Coop inst;

    // host side
    static RemoteAvatar avatar;
    static float nextThrow, volleyReady;
    // guest side
    static RemoteAvatar hostAvatar;
    static readonly Dictionary<int, EnemyGhost> ghosts = new Dictionary<int, EnemyGhost>();
    static readonly Dictionary<int, RingGhost> ringGhosts = new Dictionary<int, RingGhost>();
    static readonly HashSet<int> seenRings = new HashSet<int>();
    static float nextLocalThrow;
    static PickupGhost pickGhost; static string pickKey = "";
    static readonly HashSet<int> seen = new HashSet<int>();
    static string pendingW;
    static float bossHp = -1f; static bool bossEnraged;
    static string partnerMsg = ""; static float partnerMsgUntil;
    static Transform playerT; static Rigidbody2D playerRb;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Conn = CoopConn.Off; Role = CoopRole.None; InRun = PendingStart = WantLobby = false;
        RoomCode = Info = Err = ""; Members.Clear(); Friends.Clear(); Requests.Clear(); Sent.Clear();
        InviteFrom = InviteCode = ""; InviteUntil = 0f; GuestMoveMul = 1f;
        sock = null; helloSent = skipLeave = remoteEnd = false; myRid = -1; inst = null;
        avatar = null; hostAvatar = null; ghosts.Clear(); ringGhosts.Clear(); pickGhost = null; pickKey = ""; pendingW = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("Coop").AddComponent<Coop>(); }

    void Awake()
    {
        inst = this;
        GameState.OnReturnToMenu += OnMenu;
        GameState.OnRunStart += OnRunStartEvt;
    }
    void OnDestroy() { GameState.OnReturnToMenu -= OnMenu; GameState.OnRunStart -= OnRunStartEvt; }

    // ------------------------------------------------------------------ connection
    public static void Connect()
    {
        if (Net.State != AccountState.LoggedIn) { Err = "SIGN IN TO AN ONLINE ACCOUNT FIRST"; return; }
        if (Conn != CoopConn.Off) return;
        sock = new WebSock();
        helloSent = false;
        Conn = CoopConn.Connecting; connectStart = Time.unscaledTime;
        Err = ""; Info = "WAKING THE SERVER...";
        try { sock.Connect(ServerUrl); }
        catch (Exception e) { Fail("COULD NOT CONNECT: " + e.Message); }
    }

    public static void Disconnect()
    {
        if (sock != null) { try { sock.Close(); } catch (Exception) { } }
        sock = null; Conn = CoopConn.Off;
        ClearRoom(); Friends.Clear(); Requests.Clear(); Sent.Clear();
        if (InRun) { skipLeave = true; GameState.ReturnToMenu(false); }
    }

    static void Fail(string why)
    {
        Err = why; Info = "";
        if (sock != null) { try { sock.Close(); } catch (Exception) { } }
        sock = null; Conn = CoopConn.Off;
        ClearRoom();
        if (InRun) { skipLeave = true; GameState.ReturnToMenu(false); }
    }

    static void ClearRoom() { RoomCode = ""; Members.Clear(); Role = CoopRole.None; }

    static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }
    static void Send(string json) { if (sock != null && sock.IsOpen) sock.Send(json); }
    public static void Cmd(string t) { Send("{\"t\":\"" + t + "\"}"); }
    public static void Cmd(string t, string k, string v) { Send("{\"t\":\"" + t + "\",\"" + k + "\":\"" + Esc(v) + "\"}"); }
    static void Rt(string d) { Send("{\"t\":\"rt\",\"d\":\"" + d + "\"}"); }
    public static void AcceptInvite() { if (InviteCode.Length > 0) Cmd("join", "code", InviteCode); InviteUntil = 0f; }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (sock != null)
        {
            if (Conn == CoopConn.Connecting)
            {
                if (sock.IsOpen && !helloSent)
                {
                    helloSent = true;
                    Send("{\"t\":\"hello\",\"token\":\"" + Esc(Net.Token) + "\",\"suit\":" + GameSettings.Suit + "}");
                }
                else if (sock.IsClosed) Fail("COULD NOT REACH THE MULTIPLAYER SERVER");
                else if (Time.unscaledTime - connectStart > 90f) Fail("SERVER NOT RESPONDING - TRY AGAIN");
            }
            else if (Conn == CoopConn.Online && sock != null && sock.IsClosed) Fail("DISCONNECTED FROM THE SERVER");

            if (sock != null)
            {
                int guard = 0; string m;
                while (guard++ < 80 && sock != null && (m = sock.Poll()) != null)
                {
                    try { Handle(JsonUtility.FromJson<CoopMsg>(m)); }
                    catch (Exception e) { Debug.LogWarning("[Coop] bad message: " + e.Message); }
                }
            }
            if (Conn == CoopConn.Online && Time.unscaledTime - lastPing > 20f) { lastPing = Time.unscaledTime; Cmd("ping"); }
            if (Net.State != AccountState.LoggedIn && Conn != CoopConn.Off) Disconnect();
        }

        if (!InRun) return;
        if (IsHost) HostUpdate(); else GuestUpdate();
    }

    void Handle(CoopMsg m)
    {
        if (m == null || string.IsNullOrEmpty(m.t)) return;
        switch (m.t)
        {
            case "welcome": Conn = CoopConn.Online; Info = "CONNECTED"; Err = ""; lastPing = Time.unscaledTime; break;
            case "error":
                Err = m.msg ?? "ERROR";
                if (m.code == "auth" || m.code == "dup") Fail(Err);
                break;
            case "info": Info = m.msg ?? ""; Err = ""; break;
            case "friends":
                Friends.Clear(); Requests.Clear(); Sent.Clear();
                if (m.friends != null) Friends.AddRange(m.friends);
                if (m.requests != null) foreach (var r in m.requests) Requests.Add(r.user);
                if (m.sent != null) foreach (var s in m.sent) Sent.Add(s.user);
                break;
            case "friend_req": Info = (m.from ?? "?").ToUpper() + " SENT YOU A FRIEND REQUEST"; break;
            case "room":
                RoomCode = m.code ?? "";
                Role = m.role == "host" ? CoopRole.Host : CoopRole.Guest;
                Members.Clear();
                if (m.players != null) Members.AddRange(m.players);
                Err = "";
                break;
            case "left": ClearRoom(); break;
            case "room_closed":
                Info = (m.why ?? "ROOM CLOSED").ToUpper();
                if (InRun) { skipLeave = true; GameState.ReturnToMenu(false); }
                ClearRoom();
                break;
            case "peer_joined": Info = (m.user ?? "?").ToUpper() + " JOINED YOUR ROOM"; break;
            case "peer_left":
                Info = (m.user ?? "?").ToUpper() + " LEFT";
                if (IsHost) { DestroyAvatar(); partnerMsg = (m.user ?? "PARTNER").ToUpper() + " LEFT - YOU ARE ALONE NOW"; partnerMsgUntil = Time.unscaledTime + 5f; }
                break;
            case "start":
                Role = m.role == "host" ? CoopRole.Host : CoopRole.Guest;
                Members.Clear(); if (m.players != null) Members.AddRange(m.players);
                PendingStart = true;
                break;
            case "ended":
                if (InRun && Role == CoopRole.Guest) { remoteEnd = true; GameState.ReturnToMenu(false); }
                break;
            case "invite": InviteFrom = m.from ?? "?"; InviteCode = m.code ?? ""; InviteUntil = Time.unscaledTime + 25f; break;
            case "rt": OnData(m.d); break;
        }
    }

    // ------------------------------------------------------------------ run lifecycle
    /// <summary>Called by the UI after the transition to the arena.</summary>
    public static void BeginRun()
    {
        InRun = true;
        myRid = -1; lastSnap = Time.unscaledTime; lastW = -99f; pendingW = null;
        GuestMoveMul = 1f;
        GameState.StartRun();
        if (Role == CoopRole.Host)
        {
            var p = GameObject.FindWithTag("Player");
            Vector2 at = p != null ? (Vector2)p.transform.position : Nav.InitialStart;
            var g = FirstOther();
            DestroyAvatar();
            avatar = RemoteAvatar.Create(g != null ? g.user : "PARTNER", g != null ? g.suit : 0, true, at);
        }
        else
        {
            foreach (var c in new List<Chest>(Chest.All)) if (c != null) Destroy(c.gameObject);   // the host tells us where its chests are
        }
    }

    static CoopPlayer FirstOther()
    {
        foreach (var p in Members) if (Role == CoopRole.Host ? !p.host : p.host) return p;
        return null;
    }

    void OnRunStartEvt()
    {
        CoopRing.ClearAll();
        nextThrow = 0f; volleyReady = 0f;
    }

    void OnMenu()
    {
        bool was = InRun;
        InRun = false;
        DestroyWorld();
        if (was && Conn == CoopConn.Online)
        {
            if (Role == CoopRole.Host && !remoteEnd) Cmd("ended");
            else if (Role == CoopRole.Guest && !skipLeave) { Cmd("leave"); ClearRoom(); }
        }
        WantLobby = RoomCode.Length > 0;
        skipLeave = false; remoteEnd = false;
    }

    static void DestroyAvatar() { if (avatar != null) Destroy(avatar.gameObject); avatar = null; }

    static void DestroyWorld()
    {
        DestroyAvatar();
        if (hostAvatar != null) Destroy(hostAvatar.gameObject); hostAvatar = null;
        foreach (var g in ghosts.Values) if (g != null) Destroy(g.gameObject);
        ghosts.Clear();
        foreach (var r in ringGhosts.Values) if (r != null) Destroy(r.gameObject);
        ringGhosts.Clear();
        if (pickGhost != null) Destroy(pickGhost.gameObject); pickGhost = null; pickKey = "";
        CoopRing.ClearAll();
        GuestMoveMul = 1f; bossHp = -1f; myRid = -1;
    }

    // ------------------------------------------------------------------ host side helpers used by the game
    public static Transform Target(Transform main, Vector2 from)
    {
        if (avatar == null || !IsHost || main == null) return main;
        float a = (((Vector2)main.position) - from).sqrMagnitude;
        float b = (avatar.Pos - from).sqrMagnitude;
        return b < a ? avatar.transform : main;
    }

    public static bool AvatarNear(Vector2 p, float radius, int physLayer)
    {
        if (avatar == null || !IsHost) return false;
        if (physLayer >= 0 && avatar.gameObject.layer != physLayer) return false;
        return (avatar.Pos + new Vector2(0f, 0.4f) - p).sqrMagnitude < radius * radius;
    }

    public static Vector2 SpawnCenter(Vector2 hostPos)
    {
        if (avatar == null || !IsHost) return hostPos;
        return UnityEngine.Random.value < 0.5f ? hostPos : avatar.Pos;
    }

    public static bool FarFromAvatar(Vector2 p, float dist)
    {
        return avatar == null || !IsHost || (p - avatar.Pos).sqrMagnitude > dist * dist;
    }

    public static void SlamAvatar(Vector2 c, float R, int layer)
    {
        if (avatar == null || !IsHost || avatar.Body == null) return;
        if ((avatar.Pos - c).sqrMagnitude < R * R && avatar.gameObject.layer == layer)
            avatar.Body.Damage(Mathf.Max(1, Mathf.RoundToInt(2f * Difficulty.DamageMul)), avatar.Pos - c);
    }

    public static void NoteThrow() { lastHostThrow = Time.time; }

    public static void NavTick() { if (avatar != null && IsHost) Nav.RefreshPartner(avatar.Pos); }

    /// <summary>Guest: is a ghost enemy near this point? (for ring impact effects)</summary>
    public static bool GhostNear(Vector2 p, float r)
    {
        foreach (var g in ghosts.Values) if (g != null && ((Vector2)g.transform.position - p).sqrMagnitude < r * r) return true;
        return false;
    }

    static Vector2 GhostDir(Vector2 from)
    {
        EnemyGhost best = null; float bd = float.MaxValue;
        foreach (var g in ghosts.Values)
        {
            if (g == null) continue;
            float d = ((Vector2)g.transform.position - from).sqrMagnitude;
            if (d < bd) { bd = d; best = g; }
        }
        return best == null ? Vector2.down : ((Vector2)best.transform.position - from).normalized;
    }

    public static void SendHurt(Vector2 away) { if (IsHost) Rt("h|" + F(away.x) + "|" + F(away.y)); }

    public static void SendAnnounce(string a, string b, Color c, float secs)
    {
        if (!IsHost || avatar == null) return;
        Rt("a|" + a.Replace("|", " ").Replace("\"", "") + "|" + b.Replace("|", " ").Replace("\"", "") + "|" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "|" + F(secs));
    }

    // ------------------------------------------------------------------ formatting helpers
    static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    static float Pf(string s) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f; }
    static int Pi(string s) { int v; return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0; }

    void CachePlayer()
    {
        if (playerT != null) return;
        var p = GameObject.FindWithTag("Player");
        if (p != null) { playerT = p.transform; playerRb = p.GetComponent<Rigidbody2D>(); }
    }

    // ------------------------------------------------------------------ HOST
    void HostUpdate()
    {
        CachePlayer();
        if (avatar != null && GameState.Active)
        {
            // the guest's throws become blue rings
            if (avatar.WantVolley && Time.time >= volleyReady && CoopRing.Count <= 5 && Time.time >= nextThrow) GuestVolley();
            else if (avatar.WantThrow && Time.time >= nextThrow && CoopRing.Count < 10) GuestThrow();
        }

        if (Time.unscaledTime - lastHostSend < 1f / 20f) return;
        lastHostSend = Time.unscaledTime;
        if (avatar == null || Conn != CoopConn.Online) return;

        if (Time.unscaledTime - lastW > 4f) { lastW = Time.unscaledTime; SendWorld(); }

        var sb = new StringBuilder(600);
        Vector2 hp = playerT != null ? (Vector2)playerT.position : Vector2.zero;
        Vector2 hv = playerRb != null ? playerRb.linearVelocity : Vector2.zero;
        int hl = playerT != null ? Mathf.Clamp(playerT.gameObject.layer - 20, 0, 2) : 0;
        sb.Append("s|").Append(GameState.RunId).Append('|').Append(F(GameState.RunTime)).Append('|').Append(GameState.Score).Append('|').Append(GameState.Kills)
          .Append('|').Append(GameState.PlayerHp).Append('|').Append(GameState.PlayerMaxHp).Append('|').Append(GameState.Over ? 1 : 0)
          .Append('|').Append(F(Boosts.Move)).Append('|')
          .Append(F(hp.x)).Append(',').Append(F(hp.y)).Append(',').Append(hl).Append(',').Append(F(hv.x)).Append(',').Append(F(hv.y)).Append(',').Append(Time.time - lastHostThrow < 0.3f ? 1 : 0)
          .Append('|');
        bool first = true;
        foreach (var e in Enemy.All)
        {
            if (e == null) continue;
            if (!first) sb.Append(';'); first = false;
            Vector2 ep = e.Pos;
            sb.Append(e.Id).Append(',').Append((int)e.kind).Append(',').Append(F(ep.x)).Append(',').Append(F(ep.y)).Append(',')
              .Append(Mathf.RoundToInt(e.HpFrac * 100f)).Append(',').Append(e.Flags).Append(',').Append(Mathf.Clamp(e.gameObject.layer - 20, 0, 2));
        }
        sb.Append('|');
        first = true;
        foreach (var r in TenRing.All)
        {
            if (r == null || r.State == TenRing.RingState.Orbit) continue;
            if (!first) sb.Append(';'); first = false;
            sb.Append(r.Index).Append(',').Append(F(r.transform.position.x)).Append(',').Append(F(r.transform.position.y)).Append(",0");
        }
        foreach (var r in CoopRing.All)
        {
            if (r == null) continue;
            if (!first) sb.Append(';'); first = false;
            sb.Append(r.Id).Append(',').Append(F(r.transform.position.x)).Append(',').Append(F(r.transform.position.y)).Append(",1");
        }
        sb.Append('|').Append(BoostDirector.Snap()).Append('|');
        foreach (var c in Chest.All) sb.Append(c != null && c.Ready ? '1' : '0');
        Rt(sb.ToString());
    }

    void SendWorld()
    {
        var sb = new StringBuilder(200);
        sb.Append("w|");
        bool first = true;
        foreach (var c in Chest.All)
        {
            if (c == null) continue;
            if (!first) sb.Append(';'); first = false;
            sb.Append((int)c.Kind).Append(',').Append(F(c.transform.position.x)).Append(',').Append(F(c.transform.position.y));
        }
        sb.Append('|');
        if (Portal.Current != null) sb.Append(F(Portal.Current.Anchor.x)).Append(',').Append(F(Portal.Current.Anchor.y));
        Rt(sb.ToString());
    }

    Vector2 DirToNearest(Vector2 from)
    {
        Enemy best = null; float bd = float.MaxValue;
        foreach (var e in Enemy.All)
        {
            if (e == null) continue;
            float d = ((Vector2)e.transform.position - from).sqrMagnitude;
            if (d < bd) { bd = d; best = e; }
        }
        if (best == null) return UnityEngine.Random.insideUnitCircle.normalized;
        return ((Vector2)best.transform.position - from).normalized;
    }

    void GuestThrow()
    {
        nextThrow = Time.time + 0.3f * Boosts.FireDelay;
        Vector2 from = avatar.Pos + new Vector2(0f, 0.45f) * GS.S;
        Vector2 dir = DirToNearest(avatar.Pos);
        avatar.Throw(dir);
        CoopRing.Spawn(avatar, from, dir);
        Sfx.Play("ring_throw", 0.5f, 1.1f + UnityEngine.Random.value * 0.2f);
    }

    void GuestVolley()
    {
        volleyReady = Time.time + 4f * Boosts.VolleyCooldownMul;
        nextThrow = Time.time + 0.7f;
        Vector2 from = avatar.Pos + new Vector2(0f, 0.45f) * GS.S;
        Vector2 dir = DirToNearest(avatar.Pos);
        avatar.Throw(dir);
        float baseA = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        for (int i = 0; i < 5; i++)
        {
            float a = (baseA + (i - 2) * 22f + UnityEngine.Random.Range(-5f, 5f)) * Mathf.Deg2Rad;
            CoopRing.Spawn(avatar, from, new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
        }
        Vfx.Shockwave(from, CoopArt.Blue, 1.8f, 0.4f);
        Sfx.Play("ring_throw", 0.7f, 0.8f);
    }

    // ------------------------------------------------------------------ GUEST
    void GuestUpdate()
    {
        CachePlayer();
        if (!string.IsNullOrEmpty(pendingW) && GameState.Mode == GameMode.Playing) { ApplyWorld(pendingW); pendingW = null; }

        // lost the host?
        if (GameState.Mode == GameMode.Playing && Time.unscaledTime - lastSnap > 12f) { Fail("LOST CONNECTION TO THE HOST"); return; }

        if (Time.unscaledTime - lastGuestSend < 0.05f || Conn != CoopConn.Online || playerT == null) return;
        lastGuestSend = Time.unscaledTime;
        bool act = GameState.Active;
        Vector2 p = playerT.position;
        Vector2 v = playerRb != null ? playerRb.linearVelocity : Vector2.zero;
        bool thr = act && ((InputHub.MouseHeld && !TouchControls.Active) || InputHub.Held(KeyCode.Space) || TouchControls.ThrowHeld);
        bool vol = act && ((InputHub.RightHeld && !TouchControls.Active) || InputHub.Held(KeyCode.Q) || TouchControls.VolleyHeld);
        if ((thr || vol) && Time.time >= nextLocalThrow)
        {
            nextLocalThrow = Time.time + (vol ? 0.7f : 0.3f);
            PlayerVisual.Throw(GhostDir(p));          // instant throw pose + sound; the blue rings follow from the host
            Sfx.Play("ring_throw", 0.5f, vol ? 0.8f : 1.1f);
        }
        Rt("p|" + F(p.x) + "|" + F(p.y) + "|" + Mathf.Clamp(playerT.gameObject.layer - 20, 0, 2) + "|" + F(v.x) + "|" + F(v.y) + "|" + (Guard.Active ? 1 : 0) + "|" + (thr ? 1 : 0) + "|" + (vol ? 1 : 0));
    }

    // ------------------------------------------------------------------ data from the other side
    void OnData(string d)
    {
        if (string.IsNullOrEmpty(d)) return;
        string[] p = d.Split('|');
        switch (p[0])
        {
            case "p": if (IsHost && avatar != null && p.Length >= 9) HostGetPos(p); break;
            case "s": if (IsGuest) ApplySnapshot(p); break;
            case "w": if (IsGuest) { if (GameState.Mode == GameMode.Playing) ApplyWorld(d); else pendingW = d; } break;
            case "h": if (IsGuest && p.Length >= 3) { CachePlayer(); if (playerT != null) { var ph = playerT.GetComponent<PlayerHealth>(); if (ph != null) ph.RemoteHurt(new Vector2(Pf(p[1]), Pf(p[2]))); } } break;
            case "a":
                if (IsGuest && p.Length >= 5)
                {
                    string[] c = p[3].Split(',');
                    BoostDirector.Announce(p[1], p[2], new Color(Pf(c[0]), c.Length > 1 ? Pf(c[1]) : 1f, c.Length > 2 ? Pf(c[2]) : 1f), Pf(p[4]));
                }
                break;
        }
    }

    void HostGetPos(string[] p)
    {
        avatar.SetTarget(new Vector2(Pf(p[1]), Pf(p[2])), Pi(p[3]));
        if (avatar.Body != null) avatar.Body.Guard = p[6] == "1";
        avatar.WantThrow = p[7] == "1";
        avatar.WantVolley = p[8] == "1";
    }

    void ApplySnapshot(string[] p)
    {
        if (p.Length < 13 || GameState.Mode != GameMode.Playing) return;
        lastSnap = Time.unscaledTime;
        int rid = Pi(p[1]);
        if (myRid != rid)
        {
            bool fresh = myRid < 0;
            myRid = rid;
            if (!fresh) { GameState.StartRun(); return; }          // the host started a new round
        }
        GameState.RunTime = Pf(p[2]);
        GameState.Score = Pi(p[3]);
        GameState.Kills = Pi(p[4]);
        GameState.PlayerHp = Pi(p[5]);
        GameState.PlayerMaxHp = Mathf.Max(1, Pi(p[6]));
        GuestMoveMul = Mathf.Clamp(Pf(p[8]), 0.5f, 3f);
        if (p[7] == "1") GameState.RemoteOver();

        // host hero
        string[] h = p[9].Split(',');
        if (h.Length >= 6)
        {
            if (hostAvatar == null)
            {
                var host = FirstOther();
                hostAvatar = RemoteAvatar.Create(host != null ? host.user : "HOST", host != null ? host.suit : 0, false, new Vector2(Pf(h[0]), Pf(h[1])));
            }
            hostAvatar.SetTarget(new Vector2(Pf(h[0]), Pf(h[1])), Pi(h[2]));
            if (h[5] == "1") hostAvatar.Throw(new Vector2(Pf(h[3]), Pf(h[4])));
        }

        // enemies
        seen.Clear();
        bossHp = -1f;
        if (p[10].Length > 0)
        {
            foreach (string es in p[10].Split(';'))
            {
                string[] e = es.Split(',');
                if (e.Length < 7) continue;
                int id = Pi(e[0]);
                seen.Add(id);
                EnemyGhost g;
                Vector2 pos = new Vector2(Pf(e[2]), Pf(e[3]));
                if (!ghosts.TryGetValue(id, out g) || g == null) { g = EnemyGhost.Create(Pi(e[1]), pos); ghosts[id] = g; }
                g.Target = pos; g.SetHp(Pi(e[4]) / 100f); g.Flags = Pi(e[5]); g.SetLayer(Pi(e[6]));
                if (g.kind == 6) { bossHp = g.Hp01; bossEnraged = (g.Flags & 2) != 0; }
            }
        }
        List<int> gone = null;
        foreach (var kv in ghosts) if (!seen.Contains(kv.Key)) { if (gone == null) gone = new List<int>(); gone.Add(kv.Key); }
        if (gone != null) foreach (int id in gone) { if (ghosts[id] != null) ghosts[id].Die(); ghosts.Remove(id); }

        // rings (matched by id, so each one glides smoothly)
        seenRings.Clear();
        if (p[11].Length > 0)
        {
            foreach (string rs in p[11].Split(';'))
            {
                string[] r = rs.Split(',');
                if (r.Length < 4) continue;
                int rid2 = Pi(r[0]) + (r[3] == "1" ? 1000 : 0);
                seenRings.Add(rid2);
                Vector2 rp = new Vector2(Pf(r[1]), Pf(r[2]));
                RingGhost rg;
                if (!ringGhosts.TryGetValue(rid2, out rg) || rg == null) { rg = RingGhost.Create(rp, r[3] == "1"); ringGhosts[rid2] = rg; }
                rg.SetTarget(rp, r[3] == "1");
            }
        }
        List<int> goneRings = null;
        foreach (var kv in ringGhosts) if (!seenRings.Contains(kv.Key)) { if (goneRings == null) goneRings = new List<int>(); goneRings.Add(kv.Key); }
        if (goneRings != null) foreach (int rid3 in goneRings) { if (ringGhosts[rid3] != null) ringGhosts[rid3].Pop(); ringGhosts.Remove(rid3); }

        // boost orb
        string pk = p[12];
        if (pk != pickKey)
        {
            pickKey = pk;
            if (pickGhost != null) Destroy(pickGhost.gameObject); pickGhost = null;
            string[] b = pk.Split(',');
            if (b.Length >= 4) pickGhost = PickupGhost.Create(new Vector2(Pf(b[1]), Pf(b[2])), Pi(b[0]), Pi(b[3]));
        }

        // chests
        if (p.Length > 13)
        {
            string bits = p[13];
            for (int i = 0; i < bits.Length && i < Chest.All.Count; i++) if (Chest.All[i] != null) Chest.All[i].SetReady(bits[i] == '1');
        }
    }

    void ApplyWorld(string d)
    {
        string[] p = d.Split('|');
        if (p.Length < 3) return;
        var want = new List<Vector3>();
        if (p[1].Length > 0)
            foreach (string cs in p[1].Split(';'))
            {
                string[] c = cs.Split(',');
                if (c.Length >= 3) want.Add(new Vector3(Pf(c[1]), Pf(c[2]), Pi(c[0])));
            }
        bool same = want.Count == Chest.All.Count;
        for (int i = 0; same && i < want.Count; i++)
            if (Chest.All[i] == null || ((Vector2)Chest.All[i].transform.position - new Vector2(want[i].x, want[i].y)).sqrMagnitude > 0.25f) same = false;
        if (!same)
        {
            foreach (var c in new List<Chest>(Chest.All)) if (c != null) Destroy(c.gameObject);
            foreach (var w in want) Chest.Create(new Vector2(w.x, w.y), (ChestKind)(int)w.z);
        }
        if (p[2].Length > 0 && Portal.Current != null && !Portal.Current.IsAltar)
        {
            string[] pp = p[2].Split(',');
            if (pp.Length >= 2 && (Portal.Current.Anchor - new Vector2(Pf(pp[0]), Pf(pp[1]))).sqrMagnitude > 2f)
            {
                Destroy(Portal.Current.gameObject);
                Portal.Create(new Vector2(Pf(pp[0]), Pf(pp[1])));
            }
        }
    }

    // ------------------------------------------------------------------ in-game overlay (name tags, boss bar, partner messages)
    GUIStyle st; Texture2D pix;
    void OnGUI()
    {
        if (!InRun || GameState.Mode != GameMode.Playing || Event.current.type != EventType.Repaint) return;
        if (st == null) { st = new GUIStyle { font = GameAssets.PixelFont, alignment = TextAnchor.MiddleCenter }; pix = new Texture2D(1, 1); pix.SetPixel(0, 0, Color.white); pix.Apply(); }
        GUI.depth = -7;
        Camera cam = Camera.main;
        float sc = Mathf.Min(Screen.height / 720f, Screen.width / 1000f);
        var other = IsHost ? avatar : hostAvatar;
        if (other != null && cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(other.Pos + new Vector2(0f, 1.25f * GS.S + 0.35f));
            if (sp.z > 0f)
            {
                Color c = IsHost ? CoopArt.Blue : CoopArt.Gold;
                st.fontSize = Mathf.Max(9, Mathf.RoundToInt(11f * sc));
                var r = new Rect(sp.x - 90, Screen.height - sp.y - 10, 180, 20);
                st.normal.textColor = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), other.user.ToUpper(), st);
                st.normal.textColor = c; GUI.Label(r, other.user.ToUpper(), st);
            }
        }
        if (IsGuest && bossHp >= 0f)
        {
            float W = Screen.width / sc;
            float w = Mathf.Min(460f, W - 40f), x = (W - w) / 2f, y = 100f;
            Matrix4x4 m = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            var o = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.7f); GUI.DrawTexture(new Rect(x - 3, y - 3, w + 6, 20), pix);
            GUI.color = bossEnraged ? new Color(1f, 0.25f, 0.2f) : new Color(0.7f, 0.4f, 1f); GUI.DrawTexture(new Rect(x, y, w * bossHp, 14), pix);
            GUI.color = o;
            st.fontSize = 13; st.normal.textColor = bossEnraged ? new Color(1f, 0.5f, 0.4f) : new Color(0.9f, 0.8f, 1f);
            GUI.Label(new Rect(x, y - 25, w, 22), "THE HOLLOW KING" + (bossEnraged ? "  -  ENRAGED" : ""), st);
            GUI.matrix = m;
        }
        if (Time.unscaledTime < partnerMsgUntil)
        {
            st.fontSize = Mathf.Max(10, Mathf.RoundToInt(14f * sc));
            st.normal.textColor = new Color(1f, 0.8f, 0.4f);
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 30), partnerMsg, st);
        }
    }
}
