using UnityEngine;

// The boss fight's sound: 3D positional effects, layered impacts, world ambience and adaptive music.
//   3D AUDIO      sounds come from where they happen; the listener rides between the camera and
//                 Kaito so what is near him is loud even in a wide or aerial shot
//   LAYERS        big impacts stack a sub-bass thump, stone crackle and a rumble tail on the samples
//   AMBIENCE      wind through the temple, crackling fire pits, and the swarm's roar following Moros
//   ADAPTIVE MUSIC the battle theme muffles to a heartbeat while Kaito hides from a hunt, taiko
//                 drums join when the titan, the dragon or the arsenal appear, it dips under
//                 dialogue and cutscenes, and it is muffled while paused
[DefaultExecutionOrder(150)]
public class SoundStage : MonoBehaviour
{
    static SoundStage inst;
    AudioSource[] voices;
    int voice;
    Transform listener;
    AudioSource wind, swarm, heart, taiko;
    AudioLowPassFilter musicLP;
    AudioSource music;
    float cutoff = 22000, musicVol = 1, taikoT, heartT, lastAct;
    string prevAct = "";
    const float MusicBase = 0.42f;

    public static void Ensure()
    {
        if (inst != null || GameManager.I == null) return;
        inst = GameManager.I.gameObject.AddComponent<SoundStage>();
    }

    void Start()
    {
        AudioSynth.Make();
        // the listener: moved off the camera to a point between it and Kaito
        var camL = GameManager.I.cam.GetComponent<AudioListener>();
        if (camL) Destroy(camL);
        listener = new GameObject("Listener").AddComponent<AudioListener>().transform;
        if (Application.isMobilePlatform)
        {
            // tiny phone speakers turn the bright, noisy synth highs into a screech: roll them off
            var lp = listener.gameObject.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 4800f;
            lp.lowpassResonanceQ = 0.9f;
            AudioListener.volume = 0.8f;
        }

        voices = new AudioSource[20];
        for (int i = 0; i < voices.Length; i++)
        {
            var g = new GameObject("Voice3D");
            g.transform.SetParent(transform);
            var s = g.AddComponent<AudioSource>();
            Setup3D(s, 4f, 80f);
            voices[i] = s;
        }
        wind = Loop2D(AudioSynth.WindLoop, 0.16f);
        swarm = Loop3D(AudioSynth.SwarmLoop, 0f, 6f, 70f);
        heart = Loop2D(AudioSynth.Heartbeat, 0f);
        heart.loop = false;
        taiko = Loop2D(AudioSynth.Taiko, 0f);
        taiko.loop = false;
        foreach (var t in FindObjectsByType<Transform>())
            if (t.name == "FireAnchor")
            {
                var f = t.gameObject.AddComponent<AudioSource>();
                Setup3D(f, 2f, 18f);
                f.clip = AudioSynth.FireLoop; f.loop = true; f.volume = 0.5f; f.time = Random.value * 3f;
                f.Play();
            }
        music = GameManager.I.fx.Music;
        if (music) musicLP = music.gameObject.AddComponent<AudioLowPassFilter>();
    }

    static void Setup3D(AudioSource s, float min, float max)
    {
        s.playOnAwake = false;
        s.spatialBlend = 0.8f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = min;
        s.maxDistance = max;
        s.dopplerLevel = 0;
        s.spread = 40;
    }

    AudioSource Loop2D(AudioClip c, float vol)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = c; s.loop = true; s.volume = vol; s.spatialBlend = 0; s.playOnAwake = false;
        s.Play();
        return s;
    }

    AudioSource Loop3D(AudioClip c, float vol, float min, float max)
    {
        var g = new GameObject("Loop3D");
        g.transform.SetParent(transform);
        var s = g.AddComponent<AudioSource>();
        Setup3D(s, min, max);
        s.clip = c; s.loop = true; s.volume = vol;
        s.Play();
        return s;
    }

    // ------------------------------------------------------------------ API
    public static void At(AudioClip c, Vector3 p, float vol, float pitch = 1)
    {
        Ensure();
        if (inst == null || inst.voices == null || c == null) return;
        var s = inst.voices[inst.voice++ % inst.voices.Length];
        s.transform.position = p;
        s.pitch = pitch * Random.Range(0.96f, 1.04f);
        s.PlayOneShot(c, vol);
    }

    // a big impact: sub-bass thump + stone crack + (for the biggest) a rumble tail
    public static void Impact(Vector3 p, float size)
    {
        float k = Mathf.Clamp01(size / 7f);
        At(AudioSynth.SubThump, p, 0.55f + 0.45f * k, Mathf.Lerp(1.15f, 0.8f, k));
        At(AudioSynth.StoneCrack, p, 0.35f + 0.4f * k, Random.Range(0.85f, 1.15f));
        if (size > 3.5f) At(AudioSynth.Rumble, p, 0.3f + 0.5f * k, 1f);
    }

    public static void Explosion(Vector3 p, float size)
    {
        At(AudioSynth.Blast, p, Mathf.Clamp01(0.45f + size * 0.12f), Random.Range(0.85f, 1.05f));
        At(AudioSynth.StoneCrack, p, 0.5f, 0.9f);
    }

    public static void Stinger()
    {
        Ensure();
        if (inst == null || GameManager.I == null) return;
        GameManager.I.fx.Gong();
        inst.taiko.pitch = 0.8f;
        inst.taiko.PlayOneShot(AudioSynth.Taiko, 1f);
    }

    // ------------------------------------------------------------------ per frame
    void LateUpdate()
    {
        float dt = Clock.Dt;
        var cam = Camera.main;
        var hero = GameManager.I.p1;
        if (cam && hero)
        {
            Vector3 h = hero.Body + Vector3.up * 1.2f;
            listener.position = Vector3.Lerp(cam.transform.position, h, 0.6f);
            listener.rotation = cam.transform.rotation;
        }

        var boss = MonkBoss.Live;
        bool bossFight = boss != null && !boss.Ended;
        string act = bossFight ? boss.Act : "";
        // the swarm roar follows Moros and swells with what he is doing
        float swarmWant = 0;
        if (bossFight)
        {
            swarm.transform.position = boss.Pos + Vector3.up * 2f;
            swarmWant = act == "hover" || act == "open" || act == "melee" ? 0.22f
                      : act == "hunt" || act == "hunt_carry" ? 0.3f
                      : boss.IsTitan || act == "dragon" || act == "ascension" ? 0.75f : 0.5f;
        }
        swarm.volume = Mathf.MoveTowards(swarm.volume, swarmWant, dt * 0.8f);
        swarm.pitch = boss != null && boss.IsTitan ? 0.7f : 1f;

        // ---------------------------------------------------------------- adaptive music
        bool hunted = bossFight && boss.Hunting;
        bool chased = hunted && boss.AnyHunterNear(9f);
        bool epic = bossFight && (boss.IsTitan || act == "dragon" || act == "ascension" || act == "arsenal" || act.StartsWith("titan"));
        bool cine = GameManager.I.hud.InCinematic;
        bool lowHp = hero && hero.health < hero.maxHealth * 0.25f && hero.health > 0;
        float wantCut = TimeFX.paused ? 500 : hunted && !chased ? 950 : lowHp ? 2600 : 22000;
        float wantVol = TimeFX.paused ? 0.5f : cine ? 0.55f : hunted && !chased ? 0.7f : 1f;
        cutoff = Mathf.Exp(Mathf.MoveTowards(Mathf.Log(cutoff), Mathf.Log(wantCut), dt * 3f));
        musicVol = Mathf.MoveTowards(musicVol, wantVol, dt * 0.8f);
        if (musicLP) musicLP.cutoffFrequency = cutoff;
        if (music) music.volume = MusicBase * musicVol * GameManager.I.fx.MusicDuck;

        // heartbeat while hiding (faster when a swarm is close) or badly hurt
        if ((hunted || lowHp) && !TimeFX.paused)
        {
            heartT -= dt;
            if (heartT <= 0)
            {
                heartT = chased ? 0.48f : lowHp ? 0.62f : 0.85f;
                heart.PlayOneShot(AudioSynth.Heartbeat, chased ? 0.75f : 0.5f);
            }
        }
        // taiko drums for the titan, the dragon and the arsenal: a driving pattern
        if (epic && !TimeFX.paused)
        {
            taikoT -= dt;
            if (taikoT <= 0)
            {
                beat = (beat + 1) % 8;
                taikoT = 0.3f;
                float[] vol = { 1f, 0, 0.45f, 0.6f, 0.9f, 0, 0.5f, 0.35f };
                if (vol[beat] > 0) { taiko.pitch = beat % 4 == 0 ? 0.85f : 1.1f; taiko.PlayOneShot(AudioSynth.Taiko, vol[beat] * 0.55f); }
            }
        }
        // stings when the boss fight escalates
        if (act != prevAct)
        {
            if (act == "ascension" || act == "dragon" || act == "arsenal") Stinger();
            prevAct = act;
        }
        wind.volume = Mathf.MoveTowards(wind.volume, cine ? 0.1f : 0.16f, dt);
    }
    int beat;
}
