using System.Collections.Generic;
using UnityEngine;

/// <summary>World-anchored popup text (damage numbers, rewards). Drawn by GameUI.</summary>
public static class FloatingText
{
    public struct Item { public Vector2 pos; public string text; public Color color; public float age, life; public int size; }

    public static readonly List<Item> Items = new List<Item>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Items.Clear(); }

    public static void Add(Vector2 worldPos, string text, Color color, int size = 16, float life = 0.8f)
    {
        if (Items.Count > 60) Items.RemoveAt(0);
        Items.Add(new Item { pos = worldPos, text = text, color = color, age = 0f, life = life, size = size });
    }

    public static void Tick(float dt)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var it = Items[i];
            it.age += dt;
            if (it.age >= it.life) { Items.RemoveAt(i); continue; }
            Items[i] = it;
        }
    }
}
