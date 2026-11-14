using System.Collections.Generic;
using UnityEngine;

/// <summary>Loads fonts / sounds from Assets/Resources so nothing has to be wired up in the scene.</summary>
public static class GameAssets
{
    static Font font;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    // Assets/Resources/Fonts/PressStart2P-Regular.ttf  (SIL Open Font License)
    public static Font PixelFont
    {
        get
        {
            if (font == null) font = Resources.Load<Font>("Fonts/PressStart2P-Regular");
            return font;
        }
    }

    static readonly Dictionary<string, Sprite[]> sheets = new Dictionary<string, Sprite[]>();
    static Sprite white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { font = null; clips.Clear(); sheets.Clear(); white = null; glowSprite = null; }

    /// <summary>Slices Assets/Resources/Sprites/{name}.png into `count` square frames laid out left to right.</summary>
    public static Sprite[] Frames(string name, int count)
    {
        Sprite[] arr;
        if (sheets.TryGetValue(name, out arr) && arr != null && arr[0] != null) return arr;
        Texture2D tex = Resources.Load<Texture2D>("Sprites/" + name);
        arr = new Sprite[count];
        if (tex == null)
        {
            Debug.LogError("[GameAssets] Missing sprite sheet: Resources/Sprites/" + name);
            for (int i = 0; i < count; i++) arr[i] = WhiteLeftPivot;
        }
        else
        {
            tex.filterMode = FilterMode.Point;
            int w = tex.width / count;
            for (int i = 0; i < count; i++)
                arr[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(0.5f, 0.5f), 32f);
        }
        sheets[name] = arr;
        return arr;
    }

    static Sprite glowSprite;
    /// <summary>Soft round glow (white, alpha falls off to the edge). Tint it with the SpriteRenderer colour.</summary>
    public static Sprite Glow
    {
        get
        {
            if (glowSprite != null) return glowSprite;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            glowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 32f);
            return glowSprite;
        }
    }

    public static Texture2D Texture(string name)
    {
        Texture2D t = Resources.Load<Texture2D>("Sprites/" + name);
        if (t != null) t.filterMode = FilterMode.Point;
        return t;
    }

    public static Sprite Single(string name)
    {
        return Frames(name, 1)[0];
    }

    /// <summary>1x1 white sprite, 1 unit wide, pivot on the left edge (for bars).</summary>
    public static Sprite WhiteLeftPivot
    {
        get
        {
            if (white == null)
            {
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white); t.Apply();
                white = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
            }
            return white;
        }
    }

    public static AudioClip Clip(string name)
    {
        AudioClip c;
        if (!clips.TryGetValue(name, out c) || c == null)
        {
            c = Resources.Load<AudioClip>("Audio/" + name);
            clips[name] = c;
        }
        return c;
    }
}
