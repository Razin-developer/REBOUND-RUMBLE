using UnityEngine;

/// <summary>
/// Voice-over + scene ambience for the opening story.
/// Clips live in Resources/Audio/story: vo_{shot}_{line}.mp3 (narration) and amb_{shot}.mp3 (scene ambience, looped).
/// Generate them with tools/generate_story_audio.js (ElevenLabs). Missing clips are simply skipped.
/// Everything goes through AudioListener.volume, so MUTE silences it too.
/// </summary>
public class StoryVO : MonoBehaviour
{
    AudioSource vo, amb, ambOut;
    float ambVol = 0.5f, fadeSpeed = 0.5f;

    public static StoryVO Create(Transform parent)
    {
        var s = parent.gameObject.AddComponent<StoryVO>();
        s.vo = s.Make("VO", false);
        s.amb = s.Make("Amb", true);
        s.ambOut = s.Make("AmbOut", true);
        return s;
    }

    AudioSource Make(string n, bool loop)
    {
        var a = gameObject.AddComponent<AudioSource>();
        a.playOnAwake = false; a.loop = loop; a.spatialBlend = 0f; a.ignoreListenerPause = true;
        a.volume = 0f;
        return a;
    }

    public void Line(int shot, int line)
    {
        AudioClip c = GameAssets.Clip("story/vo_" + shot + "_" + line);
        if (c == null) return;
        vo.Stop();
        vo.clip = c;
        vo.volume = 1f;
        vo.Play();
    }

    /// <summary>Crossfade to the ambience of this shot (the old one fades out).</summary>
    public void Ambience(int shot)
    {
        AudioClip c = GameAssets.Clip("story/amb_" + shot);
        // old ambience -> fade-out source
        if (amb.isPlaying)
        {
            ambOut.Stop();
            ambOut.clip = amb.clip; ambOut.time = amb.time; ambOut.volume = amb.volume;
            ambOut.Play();
        }
        amb.Stop();
        if (c == null) return;
        amb.clip = c; amb.volume = 0f; amb.Play();
    }

    public void Stop() { vo.Stop(); amb.Stop(); ambOut.Stop(); }

    void Update()
    {
        float d = Time.unscaledDeltaTime * fadeSpeed;
        if (amb.isPlaying) amb.volume = Mathf.MoveTowards(amb.volume, ambVol, d);
        if (ambOut.isPlaying)
        {
            ambOut.volume = Mathf.MoveTowards(ambOut.volume, 0f, d * 1.5f);
            if (ambOut.volume <= 0.001f) ambOut.Stop();
        }
    }
}
