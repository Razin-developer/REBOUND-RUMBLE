using System.Collections.Generic;
using UnityEngine;

/// <summary>Tiny software painter used to draw the story's silhouette art at runtime (no image files needed).</summary>
public class Cv
{
    public readonly int w, h;
    readonly Color[] p;

    public Cv(int w, int h) { this.w = w; this.h = h; p = new Color[w * h]; }

    public Cv Fill(Color c) { for (int i = 0; i < p.Length; i++) p[i] = c; return this; }

    public Cv VGrad(Color top, Color bottom)
    {
        for (int y = 0; y < h; y++)
        {
            Color c = Color.Lerp(bottom, top, y / (float)(h - 1));
            for (int x = 0; x < w; x++) p[y * w + x] = c;
        }
        return this;
    }

    void Blend(int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        int i = y * w + x;
        if (c.a >= 0.999f) { p[i] = c; return; }
        Color d = p[i];
        float a = c.a, oa = a + d.a * (1f - a);
        if (oa < 0.0001f) return;
        p[i] = new Color((c.r * a + d.r * d.a * (1f - a)) / oa, (c.g * a + d.g * d.a * (1f - a)) / oa, (c.b * a + d.b * d.a * (1f - a)) / oa, oa);
    }

    public Cv Disc(float cx, float cy, float r, Color c) { return Ellipse(cx, cy, r, r, c); }

    public Cv Ellipse(float cx, float cy, float rx, float ry, Color c)
    {
        int x0 = Mathf.FloorToInt(cx - rx), x1 = Mathf.CeilToInt(cx + rx), y0 = Mathf.FloorToInt(cy - ry), y1 = Mathf.CeilToInt(cy + ry);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / Mathf.Max(0.01f, rx), dy = (y + 0.5f - cy) / Mathf.Max(0.01f, ry);
                if (dx * dx + dy * dy <= 1f) Blend(x, y, c);
            }
        return this;
    }

    public Cv Box(float x0, float y0, float x1, float y1, Color c)
    {
        for (int y = Mathf.Max(0, Mathf.FloorToInt(y0)); y < Mathf.Min(h, Mathf.CeilToInt(y1)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(x0)); x < Mathf.Min(w, Mathf.CeilToInt(x1)); x++) Blend(x, y, c);
        return this;
    }

    public Cv Poly(Vector2[] pts, Color c)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in pts) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
        var xs = new List<float>();
        for (int y = Mathf.Max(0, Mathf.FloorToInt(minY)); y <= Mathf.Min(h - 1, Mathf.CeilToInt(maxY)); y++)
        {
            float sy = y + 0.5f;
            xs.Clear();
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(xs[i])); x < Mathf.Min(w, Mathf.CeilToInt(xs[i + 1])); x++) Blend(x, y, c);
        }
        return this;
    }

    public Cv Line(float x0, float y0, float x1, float y1, float thick, Color c)
    {
        float len = Mathf.Max(1f, Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
        int steps = Mathf.CeilToInt(len * 1.5f);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Disc(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), thick * 0.5f, c);
        }
        return this;
    }

    public static float Hash(int n)
    {
        n = (n << 13) ^ n;
        return (1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f) * 0.5f + 0.5f;   // 0..1
    }

    public static float Noise1(float x)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i; f = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash(i), Hash(i + 1), f);
    }

    /// <summary>Fill everything below a jagged ridge line (mountains, hills, grass tops).</summary>
    public Cv Ridge(float baseY, float amp, float freq, int seed, Color c)
    {
        for (int x = 0; x < w; x++)
        {
            float n = Noise1(x * freq + seed * 17f) * 0.6f + Noise1(x * freq * 2.3f + seed * 31f) * 0.3f + Noise1(x * freq * 5.1f + seed * 7f) * 0.1f;
            int top = Mathf.Clamp(Mathf.RoundToInt(baseY + amp * n), 0, h - 1);
            for (int y = 0; y <= top; y++) Blend(x, y, c);
        }
        return this;
    }

    public Cv Glow(float cx, float cy, float r, Color c)
    {
        for (int y = Mathf.Max(0, Mathf.FloorToInt(cy - r)); y <= Mathf.Min(h - 1, Mathf.CeilToInt(cy + r)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(cx - r)); x <= Mathf.Min(w - 1, Mathf.CeilToInt(cx + r)); x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                if (d >= 1f) continue;
                float a = (1f - d); a *= a;
                Blend(x, y, new Color(c.r, c.g, c.b, c.a * a));
            }
        return this;
    }

    public Cv Speckle(int count, int seed, float yMin, float yMax, Color c)
    {
        for (int i = 0; i < count; i++)
        {
            int x = Mathf.FloorToInt(Hash(seed + i * 3) * w), y = Mathf.FloorToInt(Mathf.Lerp(yMin, yMax, Hash(seed + i * 3 + 1)));
            float b = 0.4f + 0.6f * Hash(seed + i * 3 + 2);
            Blend(x, y, new Color(c.r, c.g, c.b, c.a * b));
        }
        return this;
    }

    public Texture2D Done(bool point)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels(p);
        t.Apply();
        t.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }
}

/// <summary>Silhouette shapes for the story: people, soldiers, bats, towns, hands, cradle, map.</summary>
public static class Art
{
    // arm angle in degrees: 0 = hanging down, 90 = straight out, 180 = straight up
    static void Arm(Cv c, float sx, float sy, float side, float deg, float h, Color col)
    {
        float r = deg * Mathf.Deg2Rad;
        Vector2 d = new Vector2(Mathf.Sin(r) * side, -Mathf.Cos(r));
        Vector2 e = new Vector2(sx, sy) + d * h * 0.26f;
        c.Line(sx, sy, e.x, e.y, h * 0.06f, col);
        float r2 = (deg * 0.55f + 20f) * Mathf.Deg2Rad;
        Vector2 d2 = new Vector2(Mathf.Sin(r2) * side, -Mathf.Cos(r2));
        Vector2 f = e + d2 * h * 0.22f;
        c.Line(e.x, e.y, f.x, f.y, h * 0.052f, col);
    }

    public static void Man(Cv c, float cx, float by, float h, Color col, bool cloak, float armL, float armR, bool longHair = false, bool spiky = false)
    {
        float sy = by + h * 0.78f, wy = by + h * 0.5f, sw = h * 0.115f, ww = h * 0.07f;
        if (longHair) c.Poly(new[] { new Vector2(cx - h * 0.08f, by + h * 0.9f), new Vector2(cx + h * 0.08f, by + h * 0.9f), new Vector2(cx + h * 0.1f, by + h * 0.6f), new Vector2(cx - h * 0.1f, by + h * 0.6f) }, col);
        c.Disc(cx, by + h * 0.88f, h * 0.075f, col);
        if (spiky) for (int i = -2; i <= 2; i++) c.Poly(new[] { new Vector2(cx + i * h * 0.03f - h * 0.02f, by + h * 0.93f), new Vector2(cx + i * h * 0.03f + h * 0.02f, by + h * 0.93f), new Vector2(cx + i * h * 0.04f, by + h * 1.0f) }, col);
        c.Poly(new[] { new Vector2(cx - sw, sy), new Vector2(cx + sw, sy), new Vector2(cx + ww, wy), new Vector2(cx - ww, wy) }, col);
        if (cloak) c.Poly(new[] { new Vector2(cx - sw, sy), new Vector2(cx + sw, sy), new Vector2(cx + h * 0.2f, by), new Vector2(cx - h * 0.2f, by) }, col);
        else
        {
            c.Line(cx - ww * 0.6f, wy, cx - ww * 1.2f, by, h * 0.065f, col);
            c.Line(cx + ww * 0.6f, wy, cx + ww * 1.2f, by, h * 0.065f, col);
        }
        Arm(c, cx - sw, sy - h * 0.02f, -1f, armL, h, col);
        Arm(c, cx + sw, sy - h * 0.02f, 1f, armR, h, col);
    }

    public static void Soldier(Cv c, float cx, float by, float h, Color col, bool spear, Color eye)
    {
        Man(c, cx, by, h, col, false, 20f, spear ? 100f : 35f);
        c.Poly(new[] { new Vector2(cx - h * 0.085f, by + h * 0.9f), new Vector2(cx + h * 0.085f, by + h * 0.9f), new Vector2(cx, by + h * 1.02f) }, col);   // helmet
        if (spear)
        {
            float sx = cx + h * 0.3f;
            c.Line(sx, by + h * 0.1f, sx, by + h * 1.15f, h * 0.025f, col);
            c.Poly(new[] { new Vector2(sx - h * 0.04f, by + h * 1.15f), new Vector2(sx + h * 0.04f, by + h * 1.15f), new Vector2(sx, by + h * 1.3f) }, col);
        }
        c.Box(cx - h * 0.045f, by + h * 0.875f, cx - h * 0.015f, by + h * 0.9f, eye);
        c.Box(cx + h * 0.015f, by + h * 0.875f, cx + h * 0.045f, by + h * 0.9f, eye);
    }

    public static void Bat(Cv c, float cx, float cy, float span, Color col, Color eye)
    {
        c.Ellipse(cx, cy, span * 0.07f, span * 0.12f, col);
        c.Disc(cx, cy + span * 0.12f, span * 0.06f, col);
        for (int s = -1; s <= 1; s += 2)
        {
            c.Poly(new[]
            {
                new Vector2(cx + s * span * 0.05f, cy + span * 0.08f), new Vector2(cx + s * span * 0.5f, cy + span * 0.28f),
                new Vector2(cx + s * span * 0.42f, cy + span * 0.04f), new Vector2(cx + s * span * 0.34f, cy + span * 0.08f),
                new Vector2(cx + s * span * 0.3f, cy - span * 0.08f), new Vector2(cx + s * span * 0.18f, cy - span * 0.02f),
                new Vector2(cx + s * span * 0.1f, cy - span * 0.12f)
            }, col);
            c.Poly(new[] { new Vector2(cx + s * span * 0.03f, cy + span * 0.17f), new Vector2(cx + s * span * 0.07f, cy + span * 0.17f), new Vector2(cx + s * span * 0.06f, cy + span * 0.26f) }, col);
        }
        c.Box(cx - span * 0.04f, cy + span * 0.125f, cx - span * 0.015f, cy + span * 0.145f, eye);
        c.Box(cx + span * 0.015f, cy + span * 0.125f, cx + span * 0.04f, cy + span * 0.145f, eye);
    }

    /// <summary>Row of buildings (towers, roofs). `lit` = chance a window glows.</summary>
    public static void City(Cv c, int seed, Color col, float minH, float maxH, Color window, float lit)
    {
        float x = 0f; int i = 0;
        while (x < c.w)
        {
            float bw = 10f + Cv.Hash(seed + i * 5) * 18f, bh = Mathf.Lerp(minH, maxH, Cv.Hash(seed + i * 5 + 1));
            c.Box(x, 0, x + bw, bh, col);
            float roof = Cv.Hash(seed + i * 5 + 2);
            if (roof > 0.55f) c.Poly(new[] { new Vector2(x - 1, bh), new Vector2(x + bw + 1, bh), new Vector2(x + bw * 0.5f, bh + bw * (0.5f + roof * 0.6f)) }, col);
            else if (roof > 0.3f) c.Box(x + bw * 0.3f, bh, x + bw * 0.7f, bh + 8f + roof * 14f, col);
            for (float wy = 8f; wy < bh - 4f; wy += 9f)
                for (float wx = x + 3f; wx < x + bw - 3f; wx += 6f)
                    if (Cv.Hash(seed + (int)(wx * 3f) + (int)wy * 131) < lit) c.Box(wx, wy, wx + 2.5f, wy + 3.5f, window);
            x += bw + Cv.Hash(seed + i * 5 + 3) * 3f;
            i++;
        }
    }

    public static void Huts(Cv c, int seed, Color col, Color window)
    {
        float x = 12f; int i = 0;
        while (x < c.w - 30)
        {
            float bw = 28f + Cv.Hash(seed + i) * 20f, bh = 18f + Cv.Hash(seed + i + 1) * 14f;
            c.Box(x, 0, x + bw, bh, col);
            c.Poly(new[] { new Vector2(x - 5, bh), new Vector2(x + bw + 5, bh), new Vector2(x + bw * 0.5f, bh + 18f + Cv.Hash(seed + i + 2) * 8f) }, col);
            c.Box(x + bw * 0.55f, 4, x + bw * 0.55f + 5, 11, window);
            if (Cv.Hash(seed + i + 4) > 0.4f) c.Box(x + bw * 0.15f, 8, x + bw * 0.15f + 4, 13, window);
            x += bw + 14f + Cv.Hash(seed + i + 3) * 40f;
            if (Cv.Hash(seed + i + 6) > 0.5f)
            {
                float tx = x - 8f, th = 30f + Cv.Hash(seed + i + 7) * 18f;
                c.Line(tx, 0, tx, th * 0.5f, 3f, col);
                c.Disc(tx, th * 0.62f, 11f, col); c.Disc(tx - 7f, th * 0.5f, 8f, col); c.Disc(tx + 7f, th * 0.5f, 8f, col);
                x += 22f;
            }
            i++;
        }
    }

    // ---------------------------------------------------------- hands (palm up, fingers pointing up)
    public static readonly Vector2[] FingerPts = new Vector2[5];   // ring spots (canvas pixels) for the last hand drawn, 4 fingers + thumb

    public static void Hand(Cv c, float cx, float cy, float s, Color skin, bool mirror, bool aged)
    {
        float m = mirror ? -1f : 1f;
        Color shade = Color.Lerp(skin, Color.black, 0.35f);
        c.Box(cx - s * 0.32f, 0, cx + s * 0.32f, cy, skin);                                   // wrist / forearm
        c.Ellipse(cx, cy + s * 0.18f, s * 0.46f, s * 0.42f, skin);                            // palm
        float[] len = { 0.52f, 0.66f, 0.6f, 0.46f };
        for (int i = 0; i < 4; i++)
        {
            float bx = cx + m * (-0.33f + i * 0.22f) * s, by = cy + s * 0.42f;
            float tx = bx + m * (i - 1.5f) * 0.045f * s, ty = by + len[i] * s;
            c.Line(bx, by, tx, ty, s * 0.17f, skin);
            FingerPts[i] = new Vector2(Mathf.Lerp(bx, tx, 0.42f), Mathf.Lerp(by, ty, 0.42f));
            if (aged) { c.Line(bx - s * 0.06f, Mathf.Lerp(by, ty, 0.3f), bx + s * 0.06f, Mathf.Lerp(by, ty, 0.3f), s * 0.012f, shade); c.Line(bx - s * 0.05f, Mathf.Lerp(by, ty, 0.62f), bx + s * 0.05f, Mathf.Lerp(by, ty, 0.62f), s * 0.012f, shade); }
        }
        float thx = cx - m * 0.4f * s, thy = cy + s * 0.12f;
        c.Line(thx, thy, thx - m * 0.34f * s, thy + 0.36f * s, s * 0.19f, skin);
        FingerPts[4] = new Vector2(thx - m * 0.34f * 0.42f * s, thy + 0.36f * 0.42f * s);
        if (aged)
        {
            for (int i = 0; i < 6; i++) c.Line(cx - s * 0.3f + i * s * 0.06f, cy + s * (0.02f + 0.05f * (i % 3)), cx - s * 0.2f + i * s * 0.07f, cy + s * (0.1f + 0.04f * (i % 2)), s * 0.012f, shade);
            for (int i = 0; i < 10; i++) c.Disc(cx - s * 0.3f + Cv.Hash(i * 7) * s * 0.6f, cy + Cv.Hash(i * 7 + 1) * s * 0.5f, s * 0.018f, shade);
        }
    }

    public static void Cradle(Cv c, float cx, float by, float w, Color wood, Color blanket)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i <= 16; i++)
        {
            float a = Mathf.PI * (1f + i / 16f);
            pts.Add(new Vector2(cx + Mathf.Cos(a) * w * 0.5f, by + w * 0.32f + Mathf.Sin(a) * w * 0.28f));
        }
        c.Poly(pts.ToArray(), wood);
        c.Ellipse(cx, by + w * 0.33f, w * 0.43f, w * 0.1f, blanket);
        c.Ellipse(cx + w * 0.18f, by + w * 0.42f, w * 0.12f, w * 0.1f, new Color(blanket.r * 1.1f, blanket.g * 1.05f, blanket.b * 1.05f, 1f));   // little head
        c.Line(cx - w * 0.38f, by + w * 0.1f, cx - w * 0.52f, by, w * 0.05f, wood);
        c.Line(cx + w * 0.38f, by + w * 0.1f, cx + w * 0.52f, by, w * 0.05f, wood);
        c.Line(cx - w * 0.55f, by - 1f, cx + w * 0.55f, by - 1f, w * 0.04f, wood);
    }

    // ---------------------------------------------------------- map
    static readonly float[] Land =
    {   // cx, cy, r (fractions of the map), repeated clusters
        0.18f,0.62f,0.13f, 0.12f,0.5f,0.09f, 0.24f,0.74f,0.08f, 0.22f,0.45f,0.08f, 0.16f,0.3f,0.07f,
        0.45f,0.6f,0.1f, 0.5f,0.72f,0.08f, 0.42f,0.45f,0.08f, 0.5f,0.32f,0.09f, 0.46f,0.18f,0.06f,
        0.72f,0.62f,0.12f, 0.8f,0.5f,0.09f, 0.66f,0.72f,0.07f, 0.74f,0.42f,0.08f, 0.86f,0.7f,0.07f,
        0.62f,0.25f,0.07f, 0.8f,0.22f,0.07f, 0.3f,0.14f,0.05f, 0.9f,0.38f,0.05f
    };

    public static void MapLand(Cv c, Color col, float rim)
    {
        for (int i = 0; i + 2 < Land.Length; i += 3)
            for (int k = 0; k < 8; k++)
            {
                float a = Cv.Hash(i * 13 + k) * 6.283f, d = Cv.Hash(i * 29 + k * 3) * Land[i + 2] * 0.8f * c.w;
                float r = Land[i + 2] * c.w * (0.45f + Cv.Hash(i * 5 + k) * 0.4f);
                if (rim > 0f) c.Disc(Land[i] * c.w + Mathf.Cos(a) * d, Land[i + 1] * c.h + Mathf.Sin(a) * d, r + rim, new Color(0.25f, 0.15f, 0.07f, 1f));
            }
        for (int i = 0; i + 2 < Land.Length; i += 3)
            for (int k = 0; k < 8; k++)
            {
                float a = Cv.Hash(i * 13 + k) * 6.283f, d = Cv.Hash(i * 29 + k * 3) * Land[i + 2] * 0.8f * c.w;
                float r = Land[i + 2] * c.w * (0.45f + Cv.Hash(i * 5 + k) * 0.4f);
                c.Disc(Land[i] * c.w + Mathf.Cos(a) * d, Land[i + 1] * c.h + Mathf.Sin(a) * d, r, col);
            }
    }

    public static Vector2[] LandPoints()
    {
        var l = new List<Vector2>();
        for (int i = 0; i + 2 < Land.Length; i += 3) l.Add(new Vector2(Land[i], Land[i + 1]));
        return l.ToArray();
    }
}
