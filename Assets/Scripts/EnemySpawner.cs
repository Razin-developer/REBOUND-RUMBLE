using UnityEngine;

/// <summary>
/// Endless spawner. No waves: enemies keep arriving from off-screen and the pressure ramps up the longer you survive.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    public const int MaxEnemies = 16;   // hard limit of enemies alive at once
    Transform player;
    float nextSpawn;
    int offscreenFails;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { new GameObject("EnemySpawner").AddComponent<EnemySpawner>(); }

    void Start()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p == null) { Debug.LogError("[EnemySpawner] No Player found."); return; }
        player = p.transform;
        Nav.Init(player.position);
        GameState.OnRunStart += ClearAll;
        GameState.OnReturnToMenu += ClearAll;
    }

    void OnDestroy()
    {
        GameState.OnRunStart -= ClearAll;
        GameState.OnReturnToMenu -= ClearAll;
    }

    void ClearAll()
    {
        for (int i = Enemy.All.Count - 1; i >= 0; i--)
            if (Enemy.All[i] != null) Destroy(Enemy.All[i].gameObject);
        nextSpawn = Time.time + 1.5f;
        offscreenFails = 0;
    }

    float nextFar;

    void Update()
    {
        if (player == null) return;
        Nav.Refresh(player.position);
        Coop.NavTick();
        if (Time.time >= nextFar)       // enemies left far behind on the huge map are recycled so the cap is never wasted
        {
            nextFar = Time.time + 1f;
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                var e = Enemy.All[i];
                if (e != null && e != Enemy.Boss && !e.passive && ((Vector2)e.transform.position - (Vector2)player.position).sqrMagnitude > 55f * 55f && Coop.FarFromAvatar(e.transform.position, 55f)) Destroy(e.gameObject);
            }
        }
        if (!GameState.Active || Tutorial.Active || Coop.IsGuest) return;   // the practice level places its own enemies; a co-op guest only draws the host's enemies

        GameState.RunTime += Time.deltaTime;
        if (Time.time < nextSpawn) return;

        float t = GameState.RunTime;
        int cap = Mathf.Min(MaxEnemies + Coop.ExtraCap, 5 + (Coop.HasAvatar ? 4 : 0) + (int)(t / 15f) + GameState.Kills / 40 + (Difficulty.Frenzy ? 3 : 0));
        float interval = Mathf.Max(0.45f, (2.2f - t * 0.01f) / (Difficulty.SpawnRate));
        nextSpawn = Time.time + interval * Random.Range(0.8f, 1.2f);
        if (Enemy.All.Count >= cap) return;
        SpawnGroup(t);
    }

    EnemyKind PickKind(float t)
    {
        // weighted by how long you have survived
        float wSlime = 4f;
        float wAnt = t > 8f ? 3f : 0f;
        float wBat = t > 25f ? 2.5f : 0f;
        float wBeetle = t > 40f ? 2f : 0f;
        float wWasp = t > 55f ? 2f : 0f;
        float wGolem = t > 80f ? 1f : 0f;
        float total = wSlime + wAnt + wBat + wBeetle + wWasp + wGolem;
        float r = Random.value * total;
        if ((r -= wSlime) < 0f) return EnemyKind.Slime;
        if ((r -= wAnt) < 0f) return EnemyKind.Ant;
        if ((r -= wBat) < 0f) return EnemyKind.Bat;
        if ((r -= wBeetle) < 0f) return EnemyKind.Beetle;
        if ((r -= wWasp) < 0f) return EnemyKind.Wasp;
        return EnemyKind.Golem;
    }

    void SpawnGroup(float t)
    {
        Vector2 pos, fallback; string lay;
        bool ok = Nav.TryGetSpawnSpot(Coop.SpawnCenter(player.position), 6f, out pos, out lay, out fallback);
        if (!ok)
        {
            // only spawn on screen if nothing off-screen has been available for a while
            offscreenFails++;
            if (offscreenFails < 4 || fallback == Vector2.zero) return;
            pos = fallback; lay = GameState.ActorLayer;
        }
        offscreenFails = 0;

        float hpScale = (1f + t / 180f) * Difficulty.KillHp;
        float speedScale = Mathf.Min(1.5f, 1f + t / 600f) * Difficulty.KillSpeed;
        EnemyKind kind = PickKind(t);
        int n = kind == EnemyKind.Ant ? Random.Range(3, 6) : 1;
        n = Mathf.Min(n, MaxEnemies + Coop.ExtraCap - Enemy.All.Count);
        if (n <= 0) return;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = pos + Random.insideUnitCircle * (n > 1 ? 0.7f : 0f);
            Enemy.Spawn(kind, p, hpScale, speedScale, lay);
        }
    }
}
