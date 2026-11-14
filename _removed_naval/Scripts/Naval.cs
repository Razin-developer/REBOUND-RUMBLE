using System.Collections.Generic;
using UnityEngine;

public enum NavalPhase { Intro, Fight, Break, Victory, Defeat, Leaving }
public enum NavalEnd { Victory, Retreat, Defeat, Abort }

public class Rock { public Vector2 pos; public float r; public GameObject go; }

/// <summary>
/// THE SEA BATTLE. Board the ship at the harbour (I): the island freezes, the camera moves to open water and your ten rings
/// become the ship's weapons - FIRE (1 ring), RING DASH (2 rings, "travel") and RING BLAST (3 rings). Three crew members man swivel guns.
/// Five waves of pirate ships, upgrades in between, and the Dread Galleon at the end.
/// </summary>
public class Naval : MonoBehaviour
{
    public const int WaveCount = 5;
    public const float Radius = 64f;
    public static readonly Vector2 Center = new Vector2(900f, 900f);

    // ---- public state (read by the HUD and the ship classes) ----
    public static bool Active { get; private set; }
    public static NavalPhase Phase;
    public static int Wave;
    public static int Loot;                       // doubloons banked this voyage
    public static float HpMul = 1f;
    public static float EnemyDmg { get { return 1f + 0.06f * Mathf.Max(0, Wave - 1); } }
    public static bool EnemiesActive { get { return Active && Phase == NavalPhase.Fight; } }
    public static bool AcceptInput
    {
        get
        {
            return Active && GameState.Active && Player != null && !Player.dead &&
                   (Phase == NavalPhase.Intro || Phase == NavalPhase.Fight || Phase == NavalPhase.Break);
        }
    }

    public static Transform Root;
    public static PlayerShip Player;
    public static int BaseOrder = 2;
    public static readonly List<EnemyShip> Enemies = new List<EnemyShip>();
    public static readonly List<NavalShip> Sinking = new List<NavalShip>();
    public static readonly List<Rock> Rocks = new List<Rock>();
    public static readonly List<Ball> Balls = new List<Ball>();
    public static readonly List<Shell> Shells = new List<Shell>();
    public static readonly List<RingShot> Shots = new List<RingShot>();
    public static readonly List<BlastBomb> Blasts = new List<BlastBomb>();
    public static readonly List<DashRings> Dashes = new List<DashRings>();
    public static readonly List<RingReturn> Returns = new List<RingReturn>();
    public static readonly List<Crate> Crates = new List<Crate>();
    public static readonly List<Barrel> Barrels = new List<Barrel>();
    public static readonly Dictionary<TenRing, int> RingRole = new Dictionary<TenRing, int>();   // 1 fire, 2 dash, 3 blast

    // upgrades
    public static readonly string[] UpTitle =
    {
        "HARDWOOD HULL", "EXTRA GUNNER", "POWDER MONKEYS", "HEAVY SHOT", "BLAST MASTERY", "RING SAILS", "CARPENTER'S MALLET", "CHAIN RINGS", "IRONCLAD PROW"
    };
    public static readonly string[] UpBlurb =
    {
        "+35 MAX HULL AND REPAIR 35", "A NEW GUNNER MANS ANOTHER SWIVEL GUN", "CREW FIRES 30% FASTER", "ALL SHOTS AND RINGS HIT 25% HARDER",
        "BLAST RADIUS +25%, COOLS DOWN 20% FASTER", "RING DASH COOLS DOWN 30% FASTER, SHIP 8% FASTER", "CREW REPAIRS 1.6 HULL PER SECOND", "RINGS CUT THROUGH 2 MORE SHIPS", "RAMS HIT TWICE AS HARD, YOU TAKE HALF"
    };
    static readonly int[] UpMax = { 3, 3, 3, 3, 2, 2, 3, 2, 2 };
    public static readonly int[] UpLvl = new int[9];
    public static readonly int[] Cards = new int[3];
    public static bool CardsOpen;                 // the pick-an-upgrade screen is up
    static float blastMul = 1f, blastCdMul = 1f, dashCdMul = 1f;

    // ability timers (read by the HUD)
    public static float BlastReadyAt, DashReadyAt, NextShotAt;
    public const float BlastCd = 6f, DashCd = 4.8f;
    public static float BlastLeft { get { return Mathf.Max(0f, BlastReadyAt - Time.time); } }
    public static float DashLeft { get { return Mathf.Max(0f, DashReadyAt - Time.time); } }
    public static float BlastTotal { get { return BlastCd * blastCdMul * Boosts.VolleyCooldownMul; } }
    public static float DashTotal { get { return DashCd * dashCdMul; } }

    // banners / hud
    public static string BannerText = "", BannerSub = "";
    public static Color BannerColor = Color.white;
    public static float BannerT, BannerLife = 2.6f;
    public static float RetreatHold;              // 0..1
    public static bool RetreatHeldUI;
    public static float PhaseT;                   // seconds left in the current timed phase

    static Naval inst;
    static Transform PlayerT;
    static readonly List<NavalShip> tmp = new List<NavalShip>();
    static int enemyCounter;

    // instance
    ShipDock dock;
    GameObject playerGo;
    Rigidbody2D playerRb;
    PlayerMovement playerPm;
    Animator playerAnim;
    Camera cam;
    readonly List<Enemy> savedEnemies = new List<Enemy>();
    Vector3 landPos;
    SpriteRenderer reticle;
    LineRenderer boundary;
    struct Spawn { public float at; public ShipKind kind; }
    readonly List<Spawn> spawnQueue = new List<Spawn>();
    float clock, spawnClock, retreatHold;
    float lastTrail;
    bool torndown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = false; inst = null; Root = null; Player = null; PlayerT = null;
        Enemies.Clear(); Sinking.Clear(); Rocks.Clear(); Balls.Clear(); Shells.Clear(); Shots.Clear(); Blasts.Clear(); Dashes.Clear();
        Returns.Clear(); Crates.Clear(); Barrels.Clear(); RingRole.Clear(); Ball.ResetPool();
        Wave = 0; Loot = 0; CardsOpen = false; BannerT = 0f; RetreatHold = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        GameState.OnRunStart += Abort;
        GameState.OnReturnToMenu += Abort;
    }

    /// <summary>Why the player can't sail right now (null = they can).</summary>
    public static string WhyNot()
    {
        if (Active) return "ALREADY AT SEA";
        if (Coop.Conn == CoopConn.Online && (Coop.InRun || Coop.InRoom)) return "SEA BATTLES ARE SOLO ONLY (LEAVE THE CO-OP ROOM)";
        if (Tutorial.Active) return "NOT IN THE PRACTICE ARENA";
        if (!GameState.Active) return "NOT NOW";
        if (Time.time < ShipDock.RepairUntil) return "SHIP IN REPAIRS  " + Mathf.CeilToInt(ShipDock.RepairUntil - Time.time) + "s";
        return null;
    }

    public static void Begin(ShipDock d)
    {
        if (Active || inst != null) return;
        var pgo = GameObject.FindWithTag("Player");
        if (pgo == null) return;
        var go = new GameObject("Naval");
        inst = go.AddComponent<Naval>();
        inst.Setup(d, pgo);
    }

    /// <summary>Instant clean-up (a run starts / the menu opens while at sea).</summary>
    public static void Abort()
    {
        if (!Active || inst == null) return;
        inst.Teardown(true);
    }

    // ==================================================================================== setup / teardown
    void Setup(ShipDock d, GameObject pgo)
    {
        dock = d; playerGo = pgo; PlayerT = pgo.transform;
        playerRb = pgo.GetComponent<Rigidbody2D>(); playerPm = pgo.GetComponent<PlayerMovement>(); playerAnim = pgo.GetComponent<Animator>();
        cam = Camera.main;
        landPos = PlayerT.position;

        // freeze the island: enemies disappear from every list that targets them
        savedEnemies.Clear();
        savedEnemies.AddRange(Enemy.All);
        Enemy.All.Clear();
        foreach (var e in savedEnemies) if (e != null) e.gameObject.SetActive(false);
        if (playerPm != null) playerPm.enabled = false;
        if (playerRb != null) { playerRb.linearVelocity = Vector2.zero; playerRb.simulated = false; }
        Guard.Active = false;
        var psr = pgo.GetComponent<SpriteRenderer>();
        BaseOrder = psr != null ? psr.sortingOrder : 2;

        Root = new GameObject("Naval Root").transform;
        Enemies.Clear(); Sinking.Clear(); Rocks.Clear(); Balls.Clear(); Shells.Clear(); Shots.Clear(); Blasts.Clear(); Dashes.Clear();
        Returns.Clear(); Crates.Clear(); Barrels.Clear(); RingRole.Clear(); Ball.ResetPool(); spawnQueue.Clear();
        for (int i = 0; i < UpLvl.Length; i++) UpLvl[i] = 0;
        blastMul = blastCdMul = dashCdMul = 1f;
        Loot = 0; Wave = 0; CardsOpen = false; RetreatHold = 0f; retreatHold = 0f;
        BlastReadyAt = DashReadyAt = NextShotAt = 0f;
        HpMul = 1f + 0.25f * (Difficulty.KillHp - 1f);
        enemyCounter = 0;

        BuildArena();

        var sgo = new GameObject("PlayerShip");
        sgo.transform.SetParent(Root, false);
        Player = sgo.AddComponent<PlayerShip>();
        Player.Init(ShipSpec.Get(ShipKind.Player), 0, Center + new Vector2(0f, -30f), 90f, BaseOrder - 20, 26);
        for (int i = 0; i < 3; i++) Player.AddCrew();

        // the reticle
        var rgo = new GameObject("Reticle");
        rgo.transform.SetParent(Root, false);
        reticle = rgo.AddComponent<SpriteRenderer>();
        reticle.sprite = NavalArt.RingSprite;
        reticle.sortingLayerID = SortingLayer.NameToID(GameState.TopLayer);
        reticle.sortingOrder = 480;
        rgo.transform.localScale = Vector3.one * 0.45f;

        Active = true;
        torndown = false;
        BindPlayer();
        CameraRig.NavalCam = true;
        CameraRig.SetZoom(2.2f, false);
        CameraRig.Snap();
        MusicPlayer.Use("naval_music");
        Sfx.Play("naval_horn", 0.9f, 1f);
        StartWave(1);
    }

    void BuildArena()
    {
        NavalWater.Build(Root, Center, Radius, BaseOrder);
        // foam ring marking the edge of the battlefield
        var bgo = new GameObject("Boundary");
        bgo.transform.SetParent(Root, false);
        boundary = bgo.AddComponent<LineRenderer>();
        int n = 180;
        boundary.positionCount = n;
        boundary.loop = true;
        boundary.useWorldSpace = true;
        boundary.widthMultiplier = 0.12f;
        boundary.material = Vfx.ParticleMaterial;
        boundary.sortingLayerName = GameState.ActorLayer;
        boundary.sortingOrder = BaseOrder - 100;
        boundary.startColor = boundary.endColor = new Color(0.8f, 0.95f, 1f, 0.35f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            boundary.SetPosition(i, new Vector3(Center.x + Mathf.Cos(a) * Radius, Center.y + Mathf.Sin(a) * Radius, 0f));
        }

        // rocks (fixed layout every voyage, but the choice of sprites varies by map)
        var rnd = new System.Random(1000);
        string[] names = { "rock_a", "rock_b", "rock_c" };
        float[] radii = { 1.45f, 1.0f, 0.7f };
        int made = 0;
        for (int tries = 0; tries < 200 && made < 18; tries++)
        {
            int k = made < 2 ? 0 : (made < 5 ? 1 : 2);
            float a = (float)(rnd.NextDouble() * Mathf.PI * 2f);
            float dist = 9f + (float)rnd.NextDouble() * (Radius - 15f);
            Vector2 p = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;
            if (Vector2.Distance(p, Center + new Vector2(0f, -30f)) < 8f) continue;      // keep the start clear
            bool ok = true;
            foreach (var r in Rocks) if (Vector2.Distance(r.pos, p) < r.r + radii[k] + 4f) { ok = false; break; }
            if (!ok) continue;
            var go = new GameObject("Rock");
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(p.x, p.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, (float)(rnd.NextDouble() * 360.0));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = NavalArt.Get(names[k]);
            sr.sortingLayerID = SortingLayer.NameToID(GameState.ActorLayer);
            sr.sortingOrder = BaseOrder - 80;
            Rocks.Add(new Rock { pos = p, r = radii[k], go = go });
            made++;
        }
        SpawnBarrels(10);
    }

    static void SpawnBarrels(int count)
    {
        for (int tries = 0; tries < 60 && count > 0; tries++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float dist = Random.Range(6f, Radius - 5f);
            Vector2 p = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dist;
            bool ok = Player == null || Vector2.Distance(p, Player.pos) > 7f;
            foreach (var r in Rocks) if (Vector2.Distance(r.pos, p) < r.r + 1.5f) { ok = false; break; }
            foreach (var b in Barrels) if (!b.dead && Vector2.Distance(b.pos, p) < 4f) { ok = false; break; }
            if (!ok) continue;
            Barrels.Add(new Barrel(p));
            count--;
        }
    }

    void Teardown(bool destroyEnemies)
    {
        if (torndown) return;
        torndown = true;
        Active = false;
        CameraRig.NavalCam = false;
        CameraRig.SetZoom(1f, true);
        MusicPlayer.Use("music_loop");
        foreach (var r in TenRing.All) if (r != null) r.ForceOrbit();
        foreach (var b in Blasts) b.Cancel();
        foreach (var s in Shells) s.Cancel();
        foreach (var c in Crates) c.Destroy();
        foreach (var b in Barrels) b.Destroy();
        RingRole.Clear();
        if (Root != null) Destroy(Root.gameObject);
        Root = null; Player = null;
        Enemies.Clear(); Sinking.Clear(); Rocks.Clear(); Balls.Clear(); Shells.Clear(); Shots.Clear(); Blasts.Clear(); Dashes.Clear(); Returns.Clear(); Crates.Clear(); Barrels.Clear();
        Ball.ResetPool();

        // the island wakes up again
        foreach (var e in savedEnemies)
        {
            if (e == null) continue;
            if (destroyEnemies) { Destroy(e.gameObject); continue; }
            e.gameObject.SetActive(true);
            if (!Enemy.All.Contains(e)) Enemy.All.Add(e);
        }
        savedEnemies.Clear();
        Vector3 back = dock != null ? (Vector3)(Vector2)dock.LandSpot : landPos;
        back.z = landPos.z;
        if (playerGo != null)
        {
            PlayerT.position = back;
            if (playerRb != null) { playerRb.simulated = true; playerRb.position = back; playerRb.linearVelocity = Vector2.zero; }
            if (playerPm != null) playerPm.enabled = true;
        }
        CameraRig.Snap();
        PlayerT = null;
        inst = null;
        Destroy(gameObject);
    }

    // ==================================================================================== waves
    static readonly ShipKind[][] WaveKinds =
    {
        new[] { ShipKind.Sloop, ShipKind.Sloop, ShipKind.Sloop },
        new[] { ShipKind.Sloop, ShipKind.Sloop, ShipKind.Gunboat, ShipKind.Gunboat },
        new[] { ShipKind.Gunboat, ShipKind.Gunboat, ShipKind.Frigate, ShipKind.Mortar },
        new[] { ShipKind.Frigate, ShipKind.Frigate, ShipKind.Mortar, ShipKind.Mortar, ShipKind.Sloop, ShipKind.Sloop },
        new[] { ShipKind.Galleon, ShipKind.Gunboat, ShipKind.Gunboat }
    };

    void StartWave(int n)
    {
        Wave = n;
        Phase = NavalPhase.Intro;
        PhaseT = 3.0f;
        CardsOpen = false;
        spawnQueue.Clear();
        var kinds = WaveKinds[Mathf.Clamp(n - 1, 0, WaveKinds.Length - 1)];
        for (int i = 0; i < kinds.Length; i++) spawnQueue.Add(new Spawn { at = i * 1.1f, kind = kinds[i] });
        spawnClock = 0f;
        bool boss = n >= WaveCount;
        Announce(boss ? "WAVE " + n + " - THE DREAD GALLEON" : "WAVE " + n, boss ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.85f, 0.4f), boss ? "SINK HER AND THE SEAS ARE YOURS" : "PIRATES ON THE HORIZON");
        Sfx.Play(boss ? "naval_horn" : "naval_bell", boss ? 1f : 0.7f, boss ? 0.6f : 1f);
        if (n > 1 && Barrels.Count < 10) SpawnBarrels(3);
    }

    void WaveCleared()
    {
        int bank = 150 * Wave;
        Loot += bank;
        GameState.Score += bank;
        Sfx.Play("naval_bell", 0.9f, 1.1f);
        FloatingText.Add(Player.pos + Vector2.up * 2f, "+" + bank + " DOUBLOONS", new Color(1f, 0.85f, 0.3f), 18, 1.6f);
        float heal = Mathf.Min(Player.maxHp * 0.25f, Player.maxHp - Player.hp);
        Player.hp += heal;
        if (heal > 0.5f) FloatingText.Add(Player.pos + Vector2.up * 3f, "CREW REPAIRS THE HULL", new Color(0.5f, 1f, 0.55f), 13, 1.6f);
        if (Wave >= WaveCount)
        {
            Phase = NavalPhase.Victory; PhaseT = 4.5f;
            Announce("THE SEAS ARE YOURS!", new Color(1f, 0.85f, 0.3f), "VICTORY");
            Sfx.Play("naval_horn", 1f, 1.3f);
            Vfx.Shockwave(Player.pos, new Color(1f, 0.85f, 0.3f), 8f, 1.2f);
            return;
        }
        Phase = NavalPhase.Break;
        Announce("WAVE " + Wave + " CLEARED", new Color(0.55f, 1f, 0.6f), "CHOOSE AN UPGRADE");
        RollCards();
        CardsOpen = true;
        Crates.Add(new Crate(RandomSea(Player.pos, 5f, 9f), 0));
        if (Random.value < 0.6f) Crates.Add(new Crate(RandomSea(Player.pos, 6f, 11f), Random.value < 0.5f ? 1 : 2));
    }

    void RollCards()
    {
        var pool = new List<int>();
        for (int i = 0; i < UpTitle.Length; i++) if (UpLvl[i] < UpMax[i]) pool.Add(i);
        for (int i = 0; i < 3; i++)
        {
            if (pool.Count == 0) { Cards[i] = 0; continue; }
            int k = Random.Range(0, pool.Count);
            Cards[i] = pool[k];
            pool.RemoveAt(k);
        }
    }

    /// <summary>The player picked card i (button or 1-3).</summary>
    public static void PickCard(int i)
    {
        if (!Active || !CardsOpen || Player == null) return;
        int id = Cards[Mathf.Clamp(i, 0, 2)];
        var P = Player;
        switch (id)
        {
            case 0: P.maxHp += 35f; P.hp = Mathf.Min(P.maxHp, P.hp + 35f); break;
            case 1: P.AddCrew(); break;
            case 2: P.fireRate *= 1.3f; break;
            case 3: P.dmgMul *= 1.25f; break;
            case 4: blastMul *= 1.25f; blastCdMul *= 0.8f; break;
            case 5: dashCdMul *= 0.7f; P.speedUpgrade *= 1.08f; break;
            case 6: P.regenPerSec += 1.6f; break;
            case 7: P.pierceBonus += 2; break;
            case 8: P.ramMul *= 2f; P.ramTaken *= 0.5f; break;
        }
        UpLvl[id]++;
        CardsOpen = false;
        Sfx.Play("chest_open", 0.8f, 1.2f);
        Vfx.Shockwave(P.pos, new Color(1f, 0.9f, 0.5f), 4f, 0.6f);
        FloatingText.Add(P.pos + Vector2.up * 2f, UpTitle[id], new Color(1f, 0.9f, 0.5f), 16, 1.6f);
        Phase = NavalPhase.Break;
        PhaseT = 3.2f;                       // short breather, then the next wave
        Announce("GET READY", new Color(1f, 0.85f, 0.4f), "WAVE " + (Wave + 1) + " IN " + 3 + "...");
    }

    /// <summary>Break screen button: sail home and bank what you have.</summary>
    public static void Retreat() { if (Active && inst != null && (Phase == NavalPhase.Break || Phase == NavalPhase.Fight)) inst.Finish(NavalEnd.Retreat); }

    void Finish(NavalEnd end)
    {
        if (Phase == NavalPhase.Leaving) return;
        Phase = NavalPhase.Leaving;
        CardsOpen = false;
        string label = end == NavalEnd.Victory ? "THE SEAS ARE YOURS" : (end == NavalEnd.Defeat ? "WASHED ASHORE" : "RETURNING TO PORT");
        if (!GameUI.Transit(label, () => Complete(end))) Complete(end);
    }

    void Complete(NavalEnd end)
    {
        int loot = Loot;
        var d = dock;
        Teardown(false);
        string title, sub; Color col;
        switch (end)
        {
            case NavalEnd.Victory:
                GameState.Score += 1000;
                GameState.Heal(GameState.PlayerMaxHp);
                Boosts.Give((BoostKind)Random.Range(0, 5));
                title = "SEA BATTLE WON"; sub = "+" + (loot + 1000) + " DOUBLOONS  -  FULL HEARTS  -  A BOOST FROM THE HOLD"; col = new Color(1f, 0.85f, 0.3f);
                if (d != null) d.Rest(25f);
                break;
            case NavalEnd.Retreat:
                GameState.Heal(2);
                title = "BACK IN PORT"; sub = "+" + loot + " DOUBLOONS BANKED  -  +2 HEARTS"; col = new Color(0.6f, 0.9f, 1f);
                if (d != null) d.Rest(12f);
                break;
            default:
                GameState.Score = Mathf.Max(0, GameState.Score - loot);
                GameState.PlayerHp = Mathf.Max(1, GameState.PlayerHp - 1);
                title = "SHIP SUNK"; sub = "YOU WASHED ASHORE  -  LOST 1 HEART AND THE DOUBLOONS  -  SHIP IN REPAIRS"; col = new Color(1f, 0.45f, 0.35f);
                if (d != null) d.Rest(45f);
                break;
        }
        BoostDirector.Announce(title, sub, col, 4.5f);
    }

    // ==================================================================================== helpers used everywhere
    public static void Announce(string text, Color c, string sub = "")
    {
        BannerText = text; BannerSub = sub; BannerColor = c; BannerT = 0f; BannerLife = 2.8f;
    }

    public static void Shake(float amp, float secs) { CameraRig.Shake(amp, secs); }

    public static Vector2 Rot(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    public static Vector2 Anchor()
    {
        return PlayerT != null ? (Vector2)PlayerT.position + new Vector2(0f, 0.45f) * GS.S : Center;
    }

    public static NavalShip NearestEnemy(Vector2 from, float range)
    {
        NavalShip best = null; float bd = range;
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (e == null || e.dead) continue;
            float d = e.DistTo(from);
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    public static Vector2 Lead(Vector2 from, NavalShip tgt, float projSpeed)
    {
        float d = Vector2.Distance(from, tgt.pos);
        return tgt.pos + tgt.Vel * (d / Mathf.Max(1f, projSpeed));
    }

    public static void FireBall(Vector2 from, Vector2 dir, float speed, float dmg, int team, float life)
    {
        Balls.Add(Ball.Get(from, dir.normalized * speed, dmg, team, life));
    }

    public static void LobShell(Vector2 from, Vector2 target, float flight, float dmg, float radius)
    {
        Vector2 d = target - Center;
        if (d.magnitude > Radius - 1f) target = Center + d.normalized * (Radius - 1f);
        Shells.Add(new Shell(from, target, flight, dmg, radius));
    }

    public static Vector2 Avoid(NavalShip s)
    {
        Vector2 v = Vector2.zero;
        for (int i = 0; i < Rocks.Count; i++)
        {
            var r = Rocks[i];
            Vector2 d = s.pos - r.pos; float m = d.magnitude;
            float range = r.r + s.spec.radius + 2.5f;
            if (m < range && m > 0.01f) v += d / m * ((range - m) / range);
        }
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (e == null || e == s || e.dead) continue;
            Vector2 d = s.pos - e.pos; float m = d.magnitude;
            float range = s.spec.radius + e.spec.radius + 1.6f;
            if (m < range && m > 0.01f) v += d / m * ((range - m) / range) * 0.6f;
        }
        Vector2 off = s.pos - Center; float em = off.magnitude;
        if (em > Radius - 6f) v -= off / em * ((em - (Radius - 6f)) / 6f) * 1.5f;
        return v;
    }

    /// <summary>Keeps a ship inside the battlefield and off the rocks.</summary>
    public static void Contain(NavalShip s)
    {
        Vector2 d = s.pos - Center; float m = d.magnitude; float lim = Radius - 1.5f;
        if (m > lim) { Vector2 n = d / m; s.pos = Center + n * lim; s.speed *= 0.7f; s.push -= n * 1.0f; }
        for (int i = 0; i < Rocks.Count; i++)
        {
            var r = Rocks[i];
            Vector2 cp = NavalShip.ClosestOnSeg(s.SegA, s.SegB, r.pos);
            Vector2 off = cp - r.pos; float dist = off.magnitude;
            float min = r.r + s.spec.radius * 0.85f;
            if (dist < min)
            {
                Vector2 n = dist > 0.001f ? off / dist : Vector2.up;
                s.pos += n * (min - dist);
                s.speed *= 0.92f;
                s.ScrapeRock(r.pos + n * r.r);
            }
        }
    }

    static Vector2 RandomSea(Vector2 near, float minD, float maxD)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector2 p = near + Random.insideUnitCircle.normalized * Random.Range(minD, maxD);
            if ((p - Center).magnitude > Radius - 3f) continue;
            bool ok = true;
            foreach (var r in Rocks) if (Vector2.Distance(r.pos, p) < r.r + 1.2f) { ok = false; break; }
            if (ok) return p;
        }
        return Center + Random.insideUnitCircle * 6f;
    }

    public static EnemyShip SpawnEnemy(ShipKind kind, float angleDeg = -1f)
    {
        if (!Active || Player == null) return null;
        float a = (angleDeg < 0f ? Random.Range(0f, 360f) : angleDeg) * Mathf.Deg2Rad;
        Vector2 p = Player.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(32f, 38f);
        Vector2 off = p - Center;
        if (off.magnitude > Radius - 3f) p = Center + off.normalized * (Radius - 3f);
        if (Vector2.Distance(p, Player.pos) < 20f) p = Center + (Center - Player.pos).normalized * (Radius - 4f);   // the far side
        var go = new GameObject("Pirate");
        go.transform.SetParent(Root, false);
        var e = go.AddComponent<EnemyShip>();
        Vector2 to = Player.pos - p;
        e.Init(ShipSpec.Get(kind), 1, p, Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg, BaseOrder - 60 + (enemyCounter++ % 8) * 2, 4);
        Enemies.Add(e);
        if (kind == ShipKind.Galleon) { Sfx.Play("naval_horn", 1f, 0.55f); Shake(0.4f, 0.8f); }
        return e;
    }

    public static void ShipSunk(NavalShip s)
    {
        if (!Active) return;
        if (s.team == 1)
        {
            var e = s as EnemyShip;
            if (e != null) Enemies.Remove(e);
            Sinking.Add(s);
            GameState.AddScore(s.spec.score);
            FloatingText.Add(s.pos + Vector2.up * 1.1f, "+" + s.spec.score, new Color(1f, 0.85f, 0.3f), 18, 1.2f);
            // loot
            float r = Random.value;
            float chance = s.spec.kind == ShipKind.Sloop ? 0.15f : (s.spec.kind == ShipKind.Gunboat ? 0.28f : (s.spec.kind == ShipKind.Galleon ? 1f : 0.4f));
            if (r < chance && Player != null)
            {
                int kind = Random.value < 0.5f ? 0 : (Random.value < 0.4f ? 1 : 2);
                Crates.Add(new Crate(s.pos, kind));
                if (s.spec.kind == ShipKind.Galleon) { Crates.Add(new Crate(s.pos + Vector2.left * 1.5f, 2)); Crates.Add(new Crate(s.pos + Vector2.right * 1.5f, 1)); }
            }
        }
        else if (Phase != NavalPhase.Defeat && Phase != NavalPhase.Leaving)
        {
            Phase = NavalPhase.Defeat; PhaseT = 3.4f; CardsOpen = false;
            Announce("YOUR SHIP IS SINKING!", new Color(1f, 0.4f, 0.3f), "THE RINGS SCATTER INTO THE WAVES");
            foreach (var ring in TenRing.All) if (ring != null && ring.State != TenRing.RingState.Orbit) Return(ring);
        }
    }

    public static void Return(TenRing ring)
    {
        if (ring == null) return;
        ring.EndLoan();
        RingRole.Remove(ring);
    }

    public static void Explode(Vector2 at, float radius, float dmg, bool hurtPlayer, bool hurtEnemies)
    {
        Vfx.Burst(at, new Color(1f, 0.7f, 0.2f), 22, 6f, 0.22f, 0.7f);
        Vfx.Burst(at, new Color(0.3f, 0.3f, 0.32f, 0.9f), 14, 3f, 0.36f, 1.0f);
        Vfx.Shockwave(at, new Color(1f, 0.55f, 0.2f), radius * 1.2f, 0.45f);
        Sfx.Play("naval_boom", 0.8f, Random.Range(0.9f, 1.1f));
        float dp = Player != null ? Vector2.Distance(Player.pos, at) : 30f;
        Shake(Mathf.Clamp(0.5f - dp * 0.02f, 0.08f, 0.5f), 0.3f);

        if (hurtPlayer && Player != null && !Player.dead)
        {
            float d = Player.DistTo(at);
            if (d < radius)
            {
                Player.Damage(dmg * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(d / radius)), at);
                Player.AddPush((Player.pos - at).normalized * 3f);
            }
        }
        if (hurtEnemies)
        {
            var snap = new List<EnemyShip>(Enemies);
            foreach (var e in snap)
            {
                if (e == null || e.dead) continue;
                float d = e.DistTo(at);
                if (d >= radius) continue;
                e.Damage(dmg * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(d / radius)), at);
                e.AddPush((e.pos - at).normalized * 5f);
            }
            for (int i = 0; i < Balls.Count; i++) if (Balls[i].team == 1 && (Balls[i].pos - at).magnitude < radius) Balls[i].life = 0f;
            for (int i = 0; i < Shells.Count; i++) if (Shells[i].Alive && Vector2.Distance(Shells[i].Target, at) < radius) { Shells[i].Cancel(); Vfx.Burst(Shells[i].Target, TenRing.Gold, 8, 3f, 0.14f, 0.4f); }
        }
        for (int i = 0; i < Barrels.Count; i++)
        {
            var b = Barrels[i];
            if (!b.dead && Vector2.Distance(b.pos, at) < radius + 0.4f) b.Detonate();
        }
    }

    // ==================================================================================== main loop
    void Update()
    {
        if (!Active) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        clock += dt;
        if (BannerT < BannerLife) BannerT += dt;
        if (Player == null) return;

        UpdatePhase(dt);
        if (AcceptInput) HandleAbilities();

        // ships
        tmp.Clear();
        tmp.Add(Player);
        for (int i = 0; i < Enemies.Count; i++) if (Enemies[i] != null) tmp.Add(Enemies[i]);
        for (int i = 0; i < tmp.Count; i++) tmp[i].Think(dt);
        for (int i = 0; i < tmp.Count; i++) tmp[i].Tick(dt);
        for (int i = Sinking.Count - 1; i >= 0; i--)
        {
            if (Sinking[i] == null) { Sinking.RemoveAt(i); continue; }
            Sinking[i].Tick(dt);
        }
        ResolveShips();

        // projectiles and floating things
        for (int i = Balls.Count - 1; i >= 0; i--) if (!Balls[i].Tick(dt)) { Balls[i].Kill(); Balls.RemoveAt(i); }
        for (int i = Shells.Count - 1; i >= 0; i--) if (!Shells[i].Tick(dt)) Shells.RemoveAt(i);
        for (int i = Shots.Count - 1; i >= 0; i--) if (!Shots[i].Tick(dt)) Shots.RemoveAt(i);
        for (int i = Blasts.Count - 1; i >= 0; i--) if (!Blasts[i].Tick(dt)) Blasts.RemoveAt(i);
        for (int i = Dashes.Count - 1; i >= 0; i--) if (!Dashes[i].Tick(dt)) Dashes.RemoveAt(i);
        for (int i = Returns.Count - 1; i >= 0; i--) if (!Returns[i].Tick(dt)) Returns.RemoveAt(i);
        for (int i = Crates.Count - 1; i >= 0; i--) if (!Crates[i].Tick(dt)) Crates.RemoveAt(i);
        for (int i = Barrels.Count - 1; i >= 0; i--) if (!Barrels[i].Tick(dt)) Barrels.RemoveAt(i);

        BindPlayer();
        UpdateReticle();
    }

    void UpdatePhase(float dt)
    {
        switch (Phase)
        {
            case NavalPhase.Intro:
                PhaseT -= dt;
                if (PhaseT <= 0f) { Phase = NavalPhase.Fight; spawnClock = 0f; }
                break;
            case NavalPhase.Fight:
            {
                spawnClock += dt;
                for (int i = spawnQueue.Count - 1; i >= 0; i--)
                {
                    if (spawnQueue[i].at > spawnClock) continue;
                    var k = spawnQueue[i].kind;
                    spawnQueue.RemoveAt(i);
                    SpawnEnemy(k);
                }
                if (spawnQueue.Count == 0 && Enemies.Count == 0 && Player != null && !Player.dead) WaveCleared();
                break;
            }
            case NavalPhase.Break:
                if (!CardsOpen)
                {
                    PhaseT -= dt;
                    if (PhaseT <= 0f) StartWave(Wave + 1);
                }
                if (CardsOpen)
                {
                    if (InputHub.Pressed(KeyCode.Alpha1)) PickCard(0);
                    else if (InputHub.Pressed(KeyCode.Alpha2)) PickCard(1);
                    else if (InputHub.Pressed(KeyCode.Alpha3)) PickCard(2);
                }
                break;
            case NavalPhase.Victory:
                PhaseT -= dt;
                if (PhaseT <= 0f) Finish(NavalEnd.Victory);
                break;
            case NavalPhase.Defeat:
                PhaseT -= dt;
                if (PhaseT <= 0f) Finish(NavalEnd.Defeat);
                break;
        }

        // hold I (or the on-screen button) to retreat
        bool hold = (InputHub.Held(KeyCode.I) || RetreatHeldUI) && (Phase == NavalPhase.Fight || Phase == NavalPhase.Break) && GameState.Active;
        RetreatHeldUI = false;
        retreatHold = hold ? retreatHold + dt : Mathf.Max(0f, retreatHold - dt * 2f);
        RetreatHold = Mathf.Clamp01(retreatHold / 1.6f);
        if (retreatHold >= 1.6f) { retreatHold = 0f; Finish(NavalEnd.Retreat); }
    }

    // ==================================================================================== the ring abilities
    Vector2 AimPoint()
    {
        if (TouchControls.Active || cam == null)
        {
            var n = NearestEnemy(Player.pos, 99f);
            return n != null ? n.pos : Player.pos + Player.Fwd * 8f;
        }
        Vector2 m = InputHub.MouseScreen;
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(m.x, m.y, -cam.transform.position.z));
        return new Vector2(w.x, w.y);
    }

    void HandleAbilities()
    {
        if (Phase == NavalPhase.Intro) return;
        bool cardsUp = Phase == NavalPhase.Break && CardsOpen;
        Vector2 aim = AimPoint();
        bool fire = (InputHub.MouseHeld && !TouchControls.Active) || InputHub.Held(KeyCode.Space) || TouchControls.ThrowHeld;
        bool blast = (InputHub.RightHeld && !TouchControls.Active) || InputHub.Held(KeyCode.Q) || TouchControls.VolleyHeld;
        bool dash = InputHub.Held(KeyCode.LeftShift) || InputHub.Held(KeyCode.RightShift) || TouchControls.GuardHeld;

        // FIRE - one ring
        if (fire && !cardsUp && Time.time >= NextShotAt)
        {
            var ring = TenRing.FirstReady();
            if (ring != null)
            {
                Vector2 from = Anchor();
                Vector2 dir = aim - from;
                if (dir.sqrMagnitude < 0.04f) dir = Player.Fwd;
                ring.BeginLoan(from);
                RingRole[ring] = 1;
                Shots.Add(new RingShot(ring, from, dir, 9f * Player.dmgMul * Boosts.Damage, 3 + Player.pierceBonus + Boosts.ExtraHits));
                PlayerVisual.Throw(dir.normalized);
                Sfx.Play("ring_throw", 0.7f, 0.9f + Random.value * 0.3f);
                NextShotAt = Time.time + 0.3f * Boosts.FireDelay;
            }
        }

        // BLAST - three rings, aimed point
        if (blast && !cardsUp && Time.time >= BlastReadyAt && TenRing.ReadyCount >= 3)
        {
            var rings = TakeRings(3, 3);
            Vector2 from = Anchor();
            Vector2 to = aim;
            Vector2 dv = to - from;
            if (dv.magnitude > 15f) to = from + dv.normalized * 15f;
            else if (dv.magnitude < 3f) to = from + (dv.sqrMagnitude < 0.01f ? Player.Fwd : dv.normalized) * 3f;
            Blasts.Add(new BlastBomb(rings, from, to, 3.2f * blastMul, 34f * Player.dmgMul * Boosts.Damage));
            PlayerVisual.Throw((to - from).normalized);
            Sfx.Play("ring_throw", 0.9f, 0.7f);
            BlastReadyAt = Time.time + BlastTotal;
        }

        // DASH - two rings spin up behind the stern ("travel")
        if (dash && !Player.Dashing && Time.time >= DashReadyAt && TenRing.ReadyCount >= 2)
        {
            var rings = TakeRings(2, 2);
            Dashes.Add(new DashRings(rings, Player));
            DashReadyAt = Time.time + DashTotal;
        }
    }

    TenRing[] TakeRings(int n, int role)
    {
        var list = new List<TenRing>();
        foreach (var r in TenRing.All)
        {
            if (r == null || r.State != TenRing.RingState.Orbit) continue;
            r.BeginLoan(Anchor());
            RingRole[r] = role;
            list.Add(r);
            if (list.Count >= n) break;
        }
        return list.ToArray();
    }

    // ==================================================================================== ship vs ship
    void ResolveShips()
    {
        tmp.Clear();
        if (Player != null && !Player.dead) tmp.Add(Player);
        for (int i = 0; i < Enemies.Count; i++) if (Enemies[i] != null && !Enemies[i].dead) tmp.Add(Enemies[i]);
        for (int i = 0; i < tmp.Count; i++)
            for (int j = i + 1; j < tmp.Count; j++)
            {
                var A = tmp[i]; var B = tmp[j];
                if (A.dead || B.dead) continue;
                Vector2 a1 = A.SegA, a2 = A.SegB, am = (a1 + a2) * 0.5f, b1 = B.SegA, b2 = B.SegB, bm = (b1 + b2) * 0.5f;
                float bd = float.MaxValue; Vector2 pa = Vector2.zero, pb = Vector2.zero;
                Probe(a1, B, ref bd, ref pa, ref pb, true); Probe(am, B, ref bd, ref pa, ref pb, true); Probe(a2, B, ref bd, ref pa, ref pb, true);
                Probe(b1, A, ref bd, ref pa, ref pb, false); Probe(bm, A, ref bd, ref pa, ref pb, false); Probe(b2, A, ref bd, ref pa, ref pb, false);
                float dist = Mathf.Sqrt(bd);
                float min = A.spec.radius + B.spec.radius;
                if (dist >= min) continue;
                float depth = min - dist;
                Vector2 n = dist > 0.001f ? (pa - pb) / dist : (A.pos - B.pos).normalized;
                float ma = A.spec.radius * A.spec.radius, mb = B.spec.radius * B.spec.radius, tot = ma + mb;
                A.pos += n * depth * (mb / tot);
                B.pos -= n * depth * (ma / tot);
                if (A.team != B.team)
                {
                    Vector2 mid = (pa + pb) * 0.5f;
                    Ram(A, B, -n, mid);
                    Ram(B, A, n, mid);
                }
            }
    }

    static void Probe(Vector2 p, NavalShip other, ref float bd, ref Vector2 pa, ref Vector2 pb, bool pIsA)
    {
        Vector2 q = NavalShip.ClosestOnSeg(other.SegA, other.SegB, p);
        float d = (p - q).sqrMagnitude;
        if (d >= bd) return;
        bd = d;
        if (pIsA) { pa = p; pb = q; } else { pa = q; pb = p; }
    }

    static void Ram(NavalShip from, NavalShip to, Vector2 n, Vector2 at)
    {
        if (Time.time < from.nextRamAt) return;
        var ps = from as PlayerShip;
        float s = Mathf.Abs(from.speed);
        if (s < 1.2f && !(ps != null && ps.Dashing)) return;
        float f = 0.5f + 0.5f * Mathf.Clamp01(s / Mathf.Max(1f, from.spec.maxSpeed));
        float dmg = from.spec.ram * f;
        if (ps != null) dmg *= ps.ramMul * (ps.Dashing ? 2.4f : 1f);
        else dmg *= EnemyDmg;
        var tp = to as PlayerShip;
        if (tp != null) dmg *= tp.ramTaken;
        from.nextRamAt = Time.time + 0.9f;
        to.Damage(dmg, at);
        to.AddPush(n * 5f);
        from.AddPush(-n * 1.5f);
        Vfx.Burst(at, new Color(1f, 0.8f, 0.4f), 10, 4f, 0.14f, 0.35f);
        Sfx.Play("naval_crack", 0.6f, 0.8f);
        Shake(0.2f, 0.15f);
    }

    // ==================================================================================== player binding + reticle
    void BindPlayer()
    {
        if (PlayerT == null || Player == null) return;
        Vector2 c = Player.CaptainPos;
        PlayerT.position = new Vector3(c.x, c.y, PlayerT.position.z);
        if (playerAnim != null)
        {
            float a = Mathf.DeltaAngle(0f, Player.angle);
            int dir = (a > -45f && a <= 45f) ? 2 : (a > 45f && a <= 135f ? 1 : (a > -135f && a <= -45f ? 0 : 3));
            playerAnim.SetInteger("Direction", dir);
            playerAnim.SetBool("IsMoving", false);
        }
    }

    void UpdateReticle()
    {
        if (reticle == null) return;
        bool show = !TouchControls.Active && AcceptInput && Phase != NavalPhase.Intro && !(Phase == NavalPhase.Break && CardsOpen);
        reticle.enabled = show;
        if (!show) return;
        Vector2 aim = AimPoint();
        reticle.transform.position = new Vector3(aim.x, aim.y, 0f);
        float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 8f);
        reticle.transform.localScale = Vector3.one * 0.45f * pulse;
        bool ready = TenRing.ReadyCount > 0 && Time.time >= NextShotAt;
        reticle.color = ready ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
    }
}
