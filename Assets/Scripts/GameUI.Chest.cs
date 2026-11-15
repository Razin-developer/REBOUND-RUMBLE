using UnityEngine;

/// <summary>HUD for the kill chest: banner, progress line and the yellow "!" marker that points to the chest from the screen edge.</summary>
public partial class GameUI
{
    static Texture2D chestDisc;

    static Texture2D ChestDisc
    {
        get
        {
            if (chestDisc != null) return chestDisc;
            const int n = 64;
            chestDisc = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    chestDisc.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) / 0.04f)));
                }
            chestDisc.Apply();
            return chestDisc;
        }
    }

    void ChestCircle(Vector2 c, float r, Color col)
    {
        var oc = GUI.color; GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), ChestDisc);
        GUI.color = oc;
    }

    void DrawChestHud(float W, float H)
    {
        var rc = RewardChest.Instance;
        if (rc == null || Tutorial.Active || Coop.IsGuest || GameState.Over || page != Page.None) return;
        int pend = RewardChest.Pending;
        float t = Time.unscaledTime;

        // ---- progress line (bottom centre, above the exit line)
        if (pend > 0)
            Text(new Rect(W / 2 - 300, H - 76, 600, 20), "REWARDS WAITING IN THE CHEST  x" + pend, small, new Color(1f, 0.9f, 0.3f, 0.7f + 0.3f * Mathf.Sin(t * 8f)), TextAnchor.MiddleCenter);
        else
            Text(new Rect(W / 2 - 300, H - 76, 600, 20), "CHEST REWARD AT " + RewardChest.NextAt + " KILLS   (" + GameState.Kills + " / " + RewardChest.NextAt + ")", small, new Color(1f, 0.9f, 0.5f, 0.6f), TextAnchor.MiddleCenter);

        // ---- claimed line
        if (Time.time < RewardChest.LastRewardUntil)
            Text(new Rect(W / 2 - 380, H * 0.30f, 760, 26), RewardChest.LastRewardLine, label, Gold, TextAnchor.MiddleCenter, 13);

        // ---- banner
        if (Time.time < RewardChest.BannerUntil)
        {
            float left = RewardChest.BannerUntil - Time.time;
            float a = Mathf.Clamp01(Mathf.Min(left / 0.5f, (5f - left) / 0.25f));
            float pulse = 0.85f + 0.15f * Mathf.Sin(t * 10f);
            Rect(new Rect(0, H * 0.16f - 8, W, 84), new Color(0.15f, 0.1f, 0f, 0.62f * a));
            Rect(new Rect(0, H * 0.16f - 8, W, 3), new Color(1f, 0.85f, 0.2f, a));
            Rect(new Rect(0, H * 0.16f + 73, W, 3), new Color(1f, 0.85f, 0.2f, a));
            ChestCircle(new Vector2(W / 2 - 330, H * 0.16f + 26), 22f * pulse, new Color(1f, 0.82f, 0.1f, a));
            Text(new Rect(W / 2 - 352, H * 0.16f + 6, 44, 40), "!", title, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter, 30);
            Text(new Rect(0, H * 0.16f, W, 44), RewardChest.BannerTitle, title, new Color(1f, 0.9f, 0.35f, a), TextAnchor.MiddleCenter, 26);
            Text(new Rect(0, H * 0.16f + 44, W, 22), RewardChest.BannerSub, label, new Color(1f, 1f, 1f, a * 0.9f), TextAnchor.MiddleCenter, 11);
        }

        // ---- the "!" marker: over the chest when it is visible, on the screen edge pointing towards it when it is not
        if (pend <= 0) return;
        Camera cam = Camera.main; if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(new Vector3(rc.MarkerPos.x, rc.MarkerPos.y, 0f));
        float sx = sp.x / scale, sy = (Screen.height - sp.y) / scale;
        bool onScreen = sx > 40 && sx < W - 40 && sy > 40 && sy < H - 40;
        float pu = 1f + 0.12f * Mathf.Sin(t * 8f);
        Vector2 p; float ang = 0f;
        if (onScreen) p = new Vector2(sx, sy + Mathf.Sin(t * 5f) * 4f);
        else
        {
            Vector2 c = new Vector2(W / 2f, H / 2f), d = new Vector2(sx, sy) - c;
            if (d.sqrMagnitude < 1f) d = Vector2.up;
            float k = Mathf.Min((W / 2f - 44f) / Mathf.Max(1f, Mathf.Abs(d.x)), (H / 2f - 44f) / Mathf.Max(1f, Mathf.Abs(d.y)));
            p = c + d * k;
            ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
        if (!onScreen)
        {
            // little pointer beyond the circle
            var om = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, p * scale);
            for (int i = 0; i < 5; i++)
                Rect(new Rect(p.x + 22 + i * 3f, p.y - (5 - i) * 3.2f, 4f, (5 - i) * 6.4f), new Color(1f, 0.82f, 0.1f, 0.95f));
            GUI.matrix = om;
        }
        float r = 19f * pu;
        ChestCircle(p, r + 3f, new Color(0.45f, 0.3f, 0f, 0.9f));      // dark rim
        ChestCircle(p, r, new Color(1f, 0.82f, 0.1f, 1f));              // yellow disc
        Text(new Rect(p.x - 20, p.y - 20, 40, 40), "!", title, Color.white, TextAnchor.MiddleCenter, 24);
        if (!onScreen)
            Text(new Rect(p.x - 40, p.y + 22, 80, 16), "CHEST " + Mathf.RoundToInt(Vector2.Distance(rc.Pos, (Vector2)(cam.transform.position))) + "m", small, new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter);
    }
}
