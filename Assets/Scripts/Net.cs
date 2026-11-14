using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class PendingScore { public string id; public int score; public int seconds; public int kills; }
[Serializable] class PendingList { public List<PendingScore> items = new List<PendingScore>(); }
[Serializable] class AuthBody { public string username; public string password; }
[Serializable] class AuthReply { public string token; public string username; public string error; }
[Serializable] class ScoresBody { public List<PendingScore> scores; }
[Serializable] class ScoresReply { public string[] accepted; public string error; }
[Serializable] public class BoardEntry { public string username; public int score; public float seconds; public int kills; }
[Serializable] class BoardReply { public List<BoardEntry> entries; public string error; }

public enum AccountState { LoggedOut, LoggedIn, Offline }

/// <summary>
/// Account + score sync. Username/password only (no e-mail). Works offline: scores are queued on this
/// device and uploaded as soon as you are logged in and the server is reachable. Works in WebGL (itch.io).
/// </summary>
public class Net : MonoBehaviour
{
    // Change this to your deployed API (https required for itch.io). Players can also edit it in Settings > ACCOUNT.
    public const string DefaultUrl = "https://rebound-rumble-api.vercel.app";

    static Net I;
    public static AccountState State = AccountState.LoggedOut;
    public static string User = "", Token = "";
    public static string Message = ""; public static bool MessageIsError;
    public static bool Busy;
    public static List<BoardEntry> Board = new List<BoardEntry>();
    public static bool BoardLoaded, BoardLoading;
    static PendingList pending = new PendingList();
    static bool syncing;
    static string baseUrl = DefaultUrl;

    public static int PendingCount { get { return pending.items.Count; } }
    public static string BaseUrl { get { return baseUrl; } }
    public static string DisplayName { get { return State == AccountState.LoggedIn ? User : (State == AccountState.Offline ? "OFFLINE" : "-"); } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { I = null; Load(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (I != null) return;
        var go = new GameObject("Net");
        DontDestroyOnLoad(go);
        I = go.AddComponent<Net>();
    }

    static Net() { Load(); }

    static void Load()
    {
        baseUrl = PlayerPrefs.GetString("rr_server", DefaultUrl).TrimEnd('/');
        Token = PlayerPrefs.GetString("rr_token", "");
        User = PlayerPrefs.GetString("rr_user", "");
        State = (Token.Length > 0 && User.Length > 0) ? AccountState.LoggedIn : AccountState.LoggedOut;
        if (PlayerPrefs.GetInt("rr_offline", 0) == 1 && State == AccountState.LoggedOut) State = AccountState.Offline;
        try { pending = JsonUtility.FromJson<PendingList>(PlayerPrefs.GetString("rr_pending", "{}")) ?? new PendingList(); }
        catch (Exception) { pending = new PendingList(); }
        if (pending.items == null) pending.items = new List<PendingScore>();
    }

    static void SavePending() { PlayerPrefs.SetString("rr_pending", JsonUtility.ToJson(pending)); PlayerPrefs.Save(); }

    void Start() { if (State == AccountState.LoggedIn) SyncNow(); StartCoroutine(Periodic()); }

    IEnumerator Periodic()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(45f);
            if (State == AccountState.LoggedIn && pending.items.Count > 0) SyncNow();
        }
    }

    static void Say(string m, bool err) { Message = m; MessageIsError = err; }

    public static void SetServer(string url)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) url = DefaultUrl;
        baseUrl = url;
        PlayerPrefs.SetString("rr_server", url); PlayerPrefs.Save();
    }

    // ------------------------------------------------------------ account
    public static void Register(string u, string p) { Auth("/api/register", u, p); }
    public static void Login(string u, string p) { Auth("/api/login", u, p); }

    static void Auth(string path, string u, string p)
    {
        if (I == null || Busy) return;
        u = (u ?? "").Trim();
        if (u.Length < 3 || u.Length > 16) { Say("USERNAME: 3-16 LETTERS, NUMBERS OR _", true); return; }
        if ((p ?? "").Length < 6) { Say("PASSWORD: AT LEAST 6 CHARACTERS", true); return; }
        I.StartCoroutine(I.AuthRoutine(path, u, p));
    }

    IEnumerator AuthRoutine(string path, string u, string p)
    {
        Busy = true; Say("CONNECTING...", false);
        long code = 0; string body = "";
        yield return Post(path, JsonUtility.ToJson(new AuthBody { username = u, password = p }), null, (c, b) => { code = c; body = b; });
        Busy = false;
        if (code == 0) { Say("CAN'T REACH THE SERVER - PLAY OFFLINE OR TRY AGAIN", true); yield break; }
        AuthReply r = null;
        try { r = JsonUtility.FromJson<AuthReply>(body); } catch (Exception) { }
        if (code >= 200 && code < 300 && r != null && !string.IsNullOrEmpty(r.token))
        {
            Token = r.token; User = r.username;
            State = AccountState.LoggedIn;
            PlayerPrefs.SetString("rr_token", Token); PlayerPrefs.SetString("rr_user", User); PlayerPrefs.SetInt("rr_offline", 0); PlayerPrefs.Save();
            GameSettings.ProfileName = User.ToUpper();
            Say("WELCOME, " + User.ToUpper() + "!", false);
            SyncNow(); FetchBoard();
        }
        else Say(((r != null && !string.IsNullOrEmpty(r.error)) ? r.error : "LOGIN FAILED (" + code + ")").ToUpper(), true);
    }

    public static void PlayOffline()
    {
        State = AccountState.Offline;
        PlayerPrefs.SetInt("rr_offline", 1); PlayerPrefs.Save();
        Say("OFFLINE MODE - SCORES SYNC WHEN YOU LOG IN", false);
    }

    public static void Logout()
    {
        Token = ""; User = ""; State = AccountState.LoggedOut;
        PlayerPrefs.DeleteKey("rr_token"); PlayerPrefs.DeleteKey("rr_user"); PlayerPrefs.SetInt("rr_offline", 0); PlayerPrefs.Save();
        Say("", false);
    }

    // ------------------------------------------------------------ scores
    public static void QueueScore(int score, int seconds, int kills)
    {
        if (score <= 0) return;
        pending.items.Add(new PendingScore { id = Guid.NewGuid().ToString("N"), score = score, seconds = seconds, kills = kills });
        if (pending.items.Count > 200) pending.items.RemoveAt(0);
        SavePending();
        SyncNow();
    }

    public static void SyncNow()
    {
        if (I == null || syncing || State != AccountState.LoggedIn || pending.items.Count == 0) return;
        I.StartCoroutine(I.SyncRoutine());
    }

    IEnumerator SyncRoutine()
    {
        syncing = true;
        var batch = new List<PendingScore>(pending.items);
        if (batch.Count > 50) batch.RemoveRange(50, batch.Count - 50);
        long code = 0; string body = "";
        yield return Post("/api/scores", JsonUtility.ToJson(new ScoresBody { scores = batch }), Token, (c, b) => { code = c; body = b; });
        syncing = false;
        if (code >= 200 && code < 300)
        {
            pending.items.RemoveAll(s => batch.Exists(x => x.id == s.id));
            SavePending();
            if (pending.items.Count > 0) SyncNow();
            FetchBoard();
        }
        else if (code == 401)
        {
            Token = ""; State = AccountState.LoggedOut;
            PlayerPrefs.DeleteKey("rr_token"); PlayerPrefs.Save();
            Say("SESSION EXPIRED - PLEASE LOG IN AGAIN", true);
        }
        // code == 0 (offline) or 5xx: keep the queue and try again later
    }

    public static void FetchBoard()
    {
        if (I == null || BoardLoading) return;
        I.StartCoroutine(I.BoardRoutine());
    }

    IEnumerator BoardRoutine()
    {
        BoardLoading = true;
        using (var req = UnityWebRequest.Get(baseUrl + "/api/leaderboard?limit=10"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            BoardLoading = false;
            if (req.result == UnityWebRequest.Result.Success)
            {
                BoardReply r = null;
                try { r = JsonUtility.FromJson<BoardReply>(req.downloadHandler.text); } catch (Exception) { }
                if (r != null && r.entries != null) { Board = r.entries; BoardLoaded = true; }
            }
            else BoardLoaded = false;
        }
    }

    // ------------------------------------------------------------ http
    IEnumerator Post(string path, string json, string token, Action<long, string> done)
    {
        using (var req = new UnityWebRequest(baseUrl + path, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = 10;
            yield return req.SendWebRequest();
            bool netFail = req.result == UnityWebRequest.Result.ConnectionError;
            done(netFail ? 0 : req.responseCode, req.downloadHandler != null ? req.downloadHandler.text : "");
        }
    }
}
